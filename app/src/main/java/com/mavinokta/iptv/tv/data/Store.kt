package com.mavinokta.iptv.tv.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** Persistent settings, favourites and watch progress. Used from the main thread only. */
class Store(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences("mn_iptv_tv", Context.MODE_PRIVATE)

    var account: Account?
        get() {
            val server = prefs.getString("server", null) ?: return null
            val user = prefs.getString("user", null) ?: return null
            val pass = prefs.getString("pass", null) ?: return null
            return Account(server, user, pass)
        }
        set(value) {
            if (value != account) resetSyncDoc()
            val e = prefs.edit()
            if (value == null) {
                e.remove("server").remove("user").remove("pass")
            } else {
                e.putString("server", value.server)
                    .putString("user", value.username)
                    .putString("pass", value.password)
            }
            e.apply()
        }

    // ---- favourites ----
    // Parsed from JSON once and kept in memory; every change rewrites the stored list.

    private var favCache: List<MediaEntry>? = null
    private var favKeyCache: Set<String>? = null

    fun favorites(): List<MediaEntry> =
        favCache ?: readList(KEY_FAV).mapNotNull { MediaEntry.fromJson(it) }.also { favCache = it }

    fun favoriteKeys(): Set<String> =
        favKeyCache ?: favorites().mapTo(HashSet()) { it.key }.also { favKeyCache = it }

    fun isFavorite(key: String): Boolean = key in favoriteKeys()

    /** Returns true when the entry is now a favourite. */
    fun toggleFavorite(entry: MediaEntry): Boolean {
        val list = favorites().toMutableList()
        val idx = list.indexOfFirst { it.key == entry.key }
        val added = if (idx >= 0) {
            list.removeAt(idx)
            false
        } else {
            list.add(0, entry)
            true
        }
        favCache = list
        favKeyCache = null
        writeList(KEY_FAV, list.map { it.toJson() })
        record(SyncDoc.FAV, entry.key, JSONObject().put("e", entry.toJson()), deleted = !added)
        return added
    }

    // ---- continue watching ----

    private var progressCache: List<Progress>? = null

    fun progressList(): List<Progress> =
        progressCache ?: readList(KEY_PROGRESS).mapNotNull { Progress.fromJson(it) }
            .sortedByDescending { it.updatedAt }.also { progressCache = it }

    fun progress(key: String): Progress? = progressList().firstOrNull { it.key == key }

    fun saveProgress(p: Progress) {
        val list = progressList().filter { it.key != p.key }.toMutableList()
        list.add(0, p)
        val kept = list.take(80)
        progressCache = kept
        writeList(KEY_PROGRESS, kept.map { it.toJson() })
        record(SyncDoc.PROG, p.key, JSONObject().put("p", progressToSync(p)), deleted = false, at = p.updatedAt)
    }

    fun removeProgress(key: String) {
        val kept = progressList().filter { it.key != key }
        if (kept.size == progressList().size) return
        progressCache = kept
        writeList(KEY_PROGRESS, kept.map { it.toJson() })
        record(SyncDoc.PROG, key, JSONObject(), deleted = true)
    }

    // ---- recently watched live channels ----

    private var recentCache: List<MediaEntry>? = null

    fun recentChannels(): List<MediaEntry> =
        recentCache ?: readList(KEY_RECENT).mapNotNull { MediaEntry.fromJson(it) }.also { recentCache = it }

    fun addRecentChannel(entry: MediaEntry) {
        val list = recentChannels()
        if (list.firstOrNull()?.key == entry.key) return
        val updated = (listOf(entry) + list.filter { it.key != entry.key }).take(30)
        recentCache = updated
        writeList(KEY_RECENT, updated.map { it.toJson() })
    }

    fun clearRecentChannels() {
        recentCache = emptyList()
        prefs.edit().remove(KEY_RECENT).apply()
    }

    /** CI only: sync goes to the mock server on the emulator host. */
    var syncTestBase: String?
        get() = prefs.getString("sync_test_base", null)
        set(value) { prefs.edit().putString("sync_test_base", value).apply() }

    // ---- where the user left off ----

    var lastSection: String?
        get() = prefs.getString("last_section", null)
        set(value) { prefs.edit().putString("last_section", value).apply() }

    fun lastCategory(kind: Kind): String? = prefs.getString("last_cat_${kind.name}", null)

    fun setLastCategory(kind: Kind, id: String) {
        prefs.edit().putString("last_cat_${kind.name}", id).apply()
    }

    // ---- per-episode position / watched ----

    fun episodePosition(episodeId: String): Long = prefs.getLong("epos_$episodeId", 0L)

    fun setEpisodePosition(episodeId: String, positionMs: Long) {
        prefs.edit().putLong("epos_$episodeId", positionMs).apply()
        recordEpisode(episodeId, positionMs, isWatched(episodeId))
    }

    fun isWatched(episodeId: String): Boolean = prefs.getBoolean("ew_$episodeId", false)

    fun setWatched(episodeId: String) {
        prefs.edit().putBoolean("ew_$episodeId", true).putLong("epos_$episodeId", 0L).apply()
        recordEpisode(episodeId, 0L, true)
    }

    // ---- sync document ----
    // Every local change is also written to this document (with a time stamp, deletions as
    // tombstones); Sync merges it with the other devices' copy and hands the result back here.

    private var syncCache: JSONObject? = null

    fun syncDoc(): JSONObject {
        syncCache?.let { return it }
        val raw = prefs.getString(KEY_SYNC, null)
        val doc = raw?.let { runCatching { JSONObject(it) }.getOrNull() } ?: seedSyncDoc()
        syncCache = doc
        return doc
    }

    /** First run with sync: what this device already has goes in as very old entries. */
    private fun seedSyncDoc(): JSONObject {
        val doc = SyncDoc.empty()
        val favs = favorites()
        favs.forEachIndexed { i, e ->
            SyncDoc.section(doc, SyncDoc.FAV).put(e.key, JSONObject().put("at", (favs.size - i).toLong()).put("del", false).put("e", e.toJson()))
        }
        progressList().forEach { p ->
            SyncDoc.section(doc, SyncDoc.PROG).put(p.key, JSONObject().put("at", p.updatedAt).put("del", false).put("p", progressToSync(p)))
        }
        val eps = SyncDoc.section(doc, SyncDoc.EP)
        for (k in prefs.all.keys) {
            val id = when {
                k.startsWith("epos_") -> k.removePrefix("epos_")
                k.startsWith("ew_") -> k.removePrefix("ew_")
                else -> continue
            }
            if (eps.has(id)) continue
            val pos = prefs.getLong("epos_$id", 0L)
            val w = prefs.getBoolean("ew_$id", false)
            if (pos > 0 || w) eps.put(id, JSONObject().put("at", 1L).put("pos", pos).put("w", w))
        }
        prefs.edit().putString(KEY_SYNC, doc.toString()).apply()
        return doc
    }

    private fun saveSyncDoc(doc: JSONObject) {
        syncCache = doc
        prefs.edit().putString(KEY_SYNC, doc.toString()).apply()
    }

    private fun record(section: String, key: String, body: JSONObject, deleted: Boolean, at: Long = System.currentTimeMillis()) {
        val doc = syncDoc()
        body.put("at", at).put("del", deleted)
        SyncDoc.section(doc, section).put(key, body)
        saveSyncDoc(doc)
        Sync.localChanged()
    }

    private fun recordEpisode(id: String, pos: Long, watched: Boolean) {
        val doc = syncDoc()
        SyncDoc.section(doc, SyncDoc.EP).put(id, JSONObject().put("at", System.currentTimeMillis()).put("pos", pos).put("w", watched))
        saveSyncDoc(doc)
        Sync.localChanged()
    }

    /** Replace local favourites, progress and episode marks with the merged document. */
    fun applySyncDoc(doc: JSONObject) {
        saveSyncDoc(doc)
        val fav = SyncDoc.section(doc, SyncDoc.FAV)
        val favList = fav.keys().asSequence().mapNotNull { k ->
            val o = fav.optJSONObject(k) ?: return@mapNotNull null
            if (o.optBoolean("del")) return@mapNotNull null
            val e = o.optJSONObject("e")?.let { MediaEntry.fromJson(it) } ?: return@mapNotNull null
            o.optLong("at") to e
        }.sortedByDescending { it.first }.map { it.second }.toList()
        favCache = favList
        favKeyCache = null
        writeList(KEY_FAV, favList.map { it.toJson() })

        val prog = SyncDoc.section(doc, SyncDoc.PROG)
        val progList = prog.keys().asSequence().mapNotNull { k ->
            val o = prog.optJSONObject(k) ?: return@mapNotNull null
            if (o.optBoolean("del")) return@mapNotNull null
            o.optJSONObject("p")?.let { progressFromSync(it, o.optLong("at")) }
        }.sortedByDescending { it.updatedAt }.take(80).toList()
        progressCache = progList
        writeList(KEY_PROGRESS, progList.map { it.toJson() })

        val eps = SyncDoc.section(doc, SyncDoc.EP)
        val e = prefs.edit()
        eps.keys().forEach { id ->
            val o = eps.optJSONObject(id) ?: return@forEach
            val w = o.optBoolean("w")
            val pos = o.optLong("pos")
            if (prefs.getBoolean("ew_$id", false) != w) e.putBoolean("ew_$id", w)
            if (prefs.getLong("epos_$id", 0L) != pos) e.putLong("epos_$id", pos)
        }
        e.apply()
    }

    private fun progressToSync(p: Progress): JSONObject = JSONObject()
        .put("key", p.key).put("kind", p.kind.name).put("id", p.id).put("title", p.title)
        .put("subtitle", p.subtitle ?: "").put("image", p.image ?: "").put("ext", p.ext ?: "")
        .put("episodeId", p.episodeId ?: "").put("season", p.season).put("episode", p.episode)
        .put("pos", p.positionMs).put("dur", p.durationMs)

    private fun progressFromSync(o: JSONObject, at: Long): Progress? {
        val kind = runCatching { Kind.valueOf(o.optString("kind")) }.getOrNull() ?: return null
        val key = o.str("key") ?: return null
        val id = o.str("id") ?: return null
        return Progress(key, kind, id, o.str("title") ?: "", o.str("subtitle"), o.str("image"), o.str("ext"),
            o.str("episodeId"), o.int("season"), o.int("episode"), o.long("pos"), o.long("dur"), at)
    }

    /** A different account gets its own sync space: start it from what this device has. */
    fun resetSyncDoc() {
        syncCache = null
        prefs.edit().remove(KEY_SYNC).apply()
    }

    private fun readList(key: String): List<JSONObject> {
        val raw = prefs.getString(key, null) ?: return emptyList()
        return try {
            val a = JSONArray(raw)
            (0 until a.length()).mapNotNull { a.optJSONObject(it) }
        } catch (e: Exception) {
            emptyList()
        }
    }

    private fun writeList(key: String, items: List<JSONObject>) {
        val a = JSONArray()
        items.forEach { a.put(it) }
        prefs.edit().putString(key, a.toString()).apply()
    }

    companion object {
        private const val KEY_FAV = "favorites"
        private const val KEY_PROGRESS = "progress"
        private const val KEY_RECENT = "recent_live"
        private const val KEY_SYNC = "sync_doc"
    }
}
