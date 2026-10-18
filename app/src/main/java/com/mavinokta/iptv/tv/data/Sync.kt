package com.mavinokta.iptv.tv.data

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Base64
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.concurrent.TimeUnit
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Cross-device sync of favourites, "continue watching" and episode positions (same format on TV,
 * Mac and Windows). Everything is merged per item by time stamp and stored encrypted (AES-256-GCM,
 * key derived from the Xtream login) in one file per account in a private GitHub repository.
 */
object SyncConfig {
    const val API = "https://api.github.com"
    const val REPO = "yusufziyasaygi-oss/mn-sync"
    // XOR-obfuscated access token (contents of the mn-sync repository only). Empty: sync off.
    private const val TOKEN_X = "KidZGwwMPF1TRG0HHCghMgwJYiJJARBCWGJQUVhcIgwfEWlDHjQzSkN3agJpCQkyehxFSzQlGVl7BwJQZClQODsMbgZIX1BGWEZ1eVxfPTodBGwwSzgLb0MGUQVA"
    private const val MASK = "MN-sync-2026-key"

    fun token(): String {
        if (TOKEN_X.isEmpty()) return ""
        val x = Base64.decode(TOKEN_X, Base64.NO_WRAP)
        return String(ByteArray(x.size) { i -> (x[i].toInt() xor MASK[i % MASK.length].code).toByte() }, Charsets.UTF_8)
    }
}

object SyncDoc {
    const val FAV = "fav"
    const val PROG = "prog"
    const val EP = "ep"
    private const val TOMBSTONE_MS = 90L * 24 * 3600 * 1000

    fun empty(): JSONObject = JSONObject().put("v", 1).put(FAV, JSONObject()).put(PROG, JSONObject()).put(EP, JSONObject())

    fun section(doc: JSONObject, name: String): JSONObject =
        doc.optJSONObject(name) ?: JSONObject().also { doc.put(name, it) }

    /** Per item, the newer time stamp wins; on a tie [local] is kept. */
    fun merge(local: JSONObject, remote: JSONObject?): JSONObject {
        val out = empty()
        for (name in listOf(FAV, PROG, EP)) {
            val dst = section(out, name)
            val a = local.optJSONObject(name) ?: JSONObject()
            val b = remote?.optJSONObject(name) ?: JSONObject()
            a.keys().forEach { k -> a.optJSONObject(k)?.let { dst.put(k, it) } }
            b.keys().forEach { k ->
                val rb = b.optJSONObject(k) ?: return@forEach
                val ra = dst.optJSONObject(k)
                if (ra == null || rb.optLong("at") > ra.optLong("at")) dst.put(k, rb)
            }
        }
        prune(out)
        return out
    }

    private fun prune(doc: JSONObject) {
        val limit = System.currentTimeMillis() - TOMBSTONE_MS
        for (name in listOf(FAV, PROG)) {
            val s = section(doc, name)
            s.keys().asSequence().toList().forEach { k ->
                val o = s.optJSONObject(k)
                if (o == null || (o.optBoolean("del") && o.optLong("at") < limit)) s.remove(k)
            }
        }
        trim(section(doc, PROG), 150)
        trim(section(doc, EP), 3000)
    }

    private fun trim(s: JSONObject, max: Int) {
        if (s.length() <= max) return
        val keys = s.keys().asSequence().toList().sortedByDescending { s.optJSONObject(it)?.optLong("at") ?: 0L }
        keys.drop(max).forEach { s.remove(it) }
    }

    /** Key-order independent comparison. */
    fun same(a: JSONObject?, b: JSONObject?): Boolean = canon(a) == canon(b)

    private fun canon(v: Any?): String = when (v) {
        null, JSONObject.NULL -> "null"
        is JSONObject -> v.keys().asSequence().sorted().joinToString(",", "{", "}") { "\"$it\":" + canon(v.opt(it)) }
        is JSONArray -> (0 until v.length()).joinToString(",", "[", "]") { canon(v.opt(it)) }
        is String -> JSONObject.quote(v)
        is Number -> if (v.toDouble() == v.toLong().toDouble()) v.toLong().toString() else v.toString()
        else -> v.toString()
    }
}

object SyncCrypto {
    private fun sha256(s: String): ByteArray = MessageDigest.getInstance("SHA-256").digest(s.toByteArray(Charsets.UTF_8))

    fun fileId(a: Account): String =
        sha256("mn-sync-id-v1|${a.server.lowercase()}|${a.username}|${a.password}").joinToString("") { "%02x".format(it) }

    fun key(a: Account): ByteArray = sha256("mn-sync-key-v1|${a.server.lowercase()}|${a.username}|${a.password}")

    fun seal(doc: JSONObject, key: ByteArray): String {
        val nonce = ByteArray(12).also { SecureRandom().nextBytes(it) }
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce))
        val ct = c.doFinal(doc.toString().toByteArray(Charsets.UTF_8))
        return JSONObject()
            .put("v", 1)
            .put("n", Base64.encodeToString(nonce, Base64.NO_WRAP))
            .put("c", Base64.encodeToString(ct, Base64.NO_WRAP))
            .toString()
    }

    fun unseal(text: String, key: ByteArray): JSONObject {
        val o = JSONObject(text)
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, Base64.decode(o.getString("n"), Base64.DEFAULT)))
        return JSONObject(String(c.doFinal(Base64.decode(o.getString("c"), Base64.DEFAULT)), Charsets.UTF_8))
    }
}

/** Pull/merge/push loop. Local changes are pushed a few seconds later (at most every 45 s). */
object Sync {
    private const val TAG = "MNSync"
    private val main = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mutex = Mutex()
    private val listeners = LinkedHashSet<() -> Unit>()
    private var lastRun = 0L
    private var lastPush = 0L
    private var pending = false

    /** Set only by the CI smoke test (emulator host addresses), together with a dummy token. */
    var testBase: String? = null

    var lastStatus: String = "Kapalı"
        private set

    fun enabled(): Boolean = testBase != null || SyncConfig.token().isNotEmpty()

    fun addListener(l: () -> Unit) { listeners.add(l) }
    fun removeListener(l: () -> Unit) { listeners.remove(l) }

    private val pushRunnable = Runnable { run(force = true) }

    /** Called by Store after every local change. */
    fun localChanged() {
        if (!enabled()) return
        pending = true
        val now = SystemClock.elapsedRealtime()
        val delay = maxOf(6_000L, lastPush + 45_000L - now)
        main.removeCallbacks(pushRunnable)
        main.postDelayed(pushRunnable, delay)
    }

    /** Push now (player closed, app to background). */
    fun flush() {
        if (!enabled() || !pending) return
        main.removeCallbacks(pushRunnable)
        run(force = true)
    }

    /** Pull, unless one ran in the last [minGapMs]. */
    fun pull(minGapMs: Long = 20_000L) {
        if (!enabled()) return
        if (SystemClock.elapsedRealtime() - lastRun < minGapMs) return
        run(force = true)
    }

    private fun run(force: Boolean) {
        if (!force) return
        scope.launch { syncOnce() }
    }

    private suspend fun syncOnce() = mutex.withLock {
        val store = Repo.store
        val account = store.account ?: return@withLock
        lastRun = SystemClock.elapsedRealtime()
        val hadPending = pending
        pending = false
        try {
            val fileId = SyncCrypto.fileId(account)
            val key = SyncCrypto.key(account)
            repeat(3) {
                val (remote, sha) = withContext(Dispatchers.IO) { fetch(fileId, key) }
                val local = store.syncDoc()
                val merged = SyncDoc.merge(local, remote)
                if (!SyncDoc.same(merged, local)) {
                    store.applySyncDoc(merged)
                    listeners.toList().forEach { it() }
                }
                if (SyncDoc.same(merged, remote)) {
                    lastStatus = "Güncel"
                    return@withLock
                }
                val code = withContext(Dispatchers.IO) { upload(fileId, SyncCrypto.seal(merged, key), sha) }
                if (code == 200 || code == 201) {
                    lastPush = SystemClock.elapsedRealtime()
                    lastStatus = "Güncel"
                    return@withLock
                }
                if (code != 409 && code != 422) throw java.io.IOException("HTTP $code")
            }
            throw java.io.IOException("çakışma")
        } catch (e: Exception) {
            Log.w(TAG, "sync failed: ${e.message}")
            lastStatus = "Bağlanamadı"
            if (hadPending) {
                pending = true
                main.removeCallbacks(pushRunnable)
                main.postDelayed(pushRunnable, 60_000L)
            }
        }
    }

    private val client by lazy {
        Repo.http.newBuilder().callTimeout(25, TimeUnit.SECONDS).build()
    }

    private fun base(): String = testBase ?: SyncConfig.API
    private fun token(): String = if (testBase != null) "test" else SyncConfig.token()
    private fun url(fileId: String) = "${base()}/repos/${SyncConfig.REPO}/contents/v1/$fileId.json"

    private fun request(url: String) = Request.Builder().url(url)
        .header("Authorization", "Bearer ${token()}")
        .header("Accept", "application/vnd.github+json")
        .header("X-GitHub-Api-Version", "2022-11-28")
        .header("User-Agent", USER_AGENT)
        .header("Cache-Control", "no-cache")

    /** (document, sha); (null, null) when nothing is stored yet. A file that cannot be decrypted is overwritten. */
    private fun fetch(fileId: String, key: ByteArray): Pair<JSONObject?, String?> {
        client.newCall(request(url(fileId) + "?t=" + System.currentTimeMillis()).get().build()).execute().use { r ->
            if (r.code == 404) return null to null
            if (!r.isSuccessful) throw java.io.IOException("HTTP ${r.code}")
            val o = JSONObject(r.body?.string() ?: "{}")
            val sha = o.optString("sha").ifEmpty { null }
            val raw = String(Base64.decode(o.optString("content"), Base64.DEFAULT), Charsets.UTF_8)
            val doc = try { SyncCrypto.unseal(raw, key) } catch (e: Exception) { null }
            return doc to sha
        }
    }

    private fun upload(fileId: String, sealed: String, sha: String?): Int {
        val body = JSONObject()
            .put("message", "sync tv")
            .put("content", Base64.encodeToString(sealed.toByteArray(Charsets.UTF_8), Base64.NO_WRAP))
        if (sha != null) body.put("sha", sha)
        val req = request(url(fileId)).put(body.toString().toRequestBody("application/json".toMediaType())).build()
        client.newCall(req).execute().use { return it.code }
    }
}
