package com.mavinokta.iptv.tv.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import java.io.File
import java.io.IOException
import java.security.MessageDigest
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream
import java.util.concurrent.TimeUnit

/** App-wide singletons and in-memory catalogue cache. Accessed from the main thread. */
object Repo {

    lateinit var store: Store
        private set

    val http: OkHttpClient by lazy {
        OkHttpClient.Builder()
            .connectTimeout(12, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .followRedirects(true)
            .followSslRedirects(true)
            .retryOnConnectionFailure(true)
            .build()
    }

    private var cachedApi: XtreamApi? = null
    private val categoryCache = HashMap<Kind, List<Category>>()
    private val streamCache = HashMap<String, List<MediaEntry>>()
    private val seriesCache = HashMap<String, SeriesInfo>()
    private val searchNames = HashMap<Kind, Pair<List<MediaEntry>, Array<String>>>()

    // One download per list at a time: "Tümü" and search, or the same category picked twice, share
    // a request. It is cancelled when nobody waits for it any more.
    private class Flight<T>(val deferred: Deferred<T>) { var waiters = 0 }
    private val flights = HashMap<String, Flight<*>>()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private suspend fun <T> shared(key: String, load: suspend () -> T): T {
        @Suppress("UNCHECKED_CAST")
        val flight = (flights[key] as Flight<T>?) ?: Flight(scope.async { load() }).also { f ->
            flights[key] = f
            f.deferred.invokeOnCompletion { if (flights[key] === f) flights.remove(key) }
        }
        flight.waiters++
        try {
            return flight.deferred.await()
        } finally {
            flight.waiters--
            if (flight.waiters == 0 && !flight.deferred.isCompleted) {
                if (flights[key] === flight) flights.remove(key)
                flight.deferred.cancel()
            }
        }
    }

    // ---- disk cache -------------------------------------------------------------------------
    // Raw panel answers are kept (gzip) so the app opens with the last known lists at once, even
    // when the panel is slow or unreachable. Fresh enough lists are used as they are; older ones are
    // shown immediately and refreshed in the background (the next visit, or onFresh, sees the update).

    private var cacheDir: File? = null
    private val refreshing = HashSet<String>()

    private const val FRESH_MS = 10 * 60_000L
    private const val USABLE_MS = 3 * 24 * 3_600_000L
    private const val PRUNE_MS = 14 * 24 * 3_600_000L

    fun init(context: Context) {
        if (!::store.isInitialized) {
            store = Store(context)
            Sync.testBase = store.syncTestBase
        }
        if (cacheDir == null) {
            val dir = File(context.applicationContext.cacheDir, "xtream").also { it.mkdirs() }
            cacheDir = dir
            scope.launch(Dispatchers.IO) {
                val limit = System.currentTimeMillis() - PRUNE_MS
                dir.listFiles()?.forEach { if (it.lastModified() < limit) it.delete() }
            }
        }
    }

    // An empty answer is often a panel hiccup (connection limit, error page); never keep it.
    private fun worthKeeping(value: Any): Boolean = !(value is Collection<*> && value.isEmpty())

    private class Disk(val body: String, val ageMs: Long)

    private fun diskFile(api: XtreamApi, name: String): File? {
        val dir = cacheDir ?: return null
        val sha = MessageDigest.getInstance("SHA-1").digest("${api.account.server}|${api.account.username}|$name".toByteArray())
        return File(dir, sha.joinToString("") { "%02x".format(it) } + ".gz")
    }

    private suspend fun readDisk(api: XtreamApi, name: String): Disk? = withContext(Dispatchers.IO) {
        val f = diskFile(api, name) ?: return@withContext null
        if (!f.isFile) return@withContext null
        try {
            val body = GZIPInputStream(f.inputStream().buffered()).use { it.readBytes().toString(Charsets.UTF_8) }
            Disk(body, System.currentTimeMillis() - f.lastModified())
        } catch (e: Exception) {
            f.delete()
            null
        }
    }

    private suspend fun writeDisk(api: XtreamApi, name: String, body: String) = withContext(Dispatchers.IO) {
        val f = diskFile(api, name) ?: return@withContext
        try {
            val tmp = File(f.parentFile, f.name + ".tmp")
            GZIPOutputStream(tmp.outputStream().buffered()).use { it.write(body.toByteArray(Charsets.UTF_8)) }
            if (!tmp.renameTo(f)) tmp.delete()
        } catch (e: Exception) {
            // The cache is an optimisation only.
        }
    }

    /**
     * Memory → disk → network. [fetch] gets the raw answer, [parse] turns it into the value.
     * [onFresh] is called when a background refresh produced something different from what was returned.
     */
    private suspend fun <T : Any> cached(
        api: XtreamApi,
        name: String,
        memory: () -> T?,
        remember: (T) -> Unit,
        fetch: suspend () -> String,
        parse: suspend (String) -> T,
        onFresh: ((T) -> Unit)? = null
    ): T {
        memory()?.let { return it }
        val disk = readDisk(api, name)
        if (disk != null && disk.ageMs < USABLE_MS) {
            val value = try {
                parse(disk.body)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                null
            }
            if (value != null) {
                if (cachedApi === api) remember(value)
                if (disk.ageMs > FRESH_MS && refreshing.add(name)) {
                    scope.launch {
                        try {
                            val fresh = shared("refresh|$name") {
                                val body = fetch()
                                val parsed = parse(body)
                                if (worthKeeping(parsed)) writeDisk(api, name, body)
                                parsed
                            }
                            if (cachedApi === api && fresh != value) {
                                remember(fresh)
                                onFresh?.invoke(fresh)
                            }
                        } catch (e: CancellationException) {
                            // cache cleared or screen left
                        } catch (e: Exception) {
                            // keep showing the cached list
                        } finally {
                            refreshing.remove(name)
                        }
                    }
                }
                return value
            }
        }
        return try {
            val value = shared("load|$name") {
                val body = fetch()
                val parsed = parse(body)
                if (worthKeeping(parsed)) writeDisk(api, name, body)
                parsed
            }
            if (cachedApi === api) remember(value)
            value
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Panel unreachable: an old list is better than an error screen.
            val old = disk?.let { runCatching { parse(it.body) }.getOrNull() }
            if (old != null) {
                if (cachedApi === api) remember(old)
                old
            } else {
                throw e
            }
        }
    }

    fun api(): XtreamApi {
        val account = store.account ?: throw IOException("Hesap bilgisi yok")
        val current = cachedApi
        if (current != null && current.account == account) return current
        clearCache()
        return XtreamApi(account, http).also { cachedApi = it }
    }

    /** Forget everything, including the disk cache, so the next load asks the panel. */
    fun refreshAll() {
        clearCache()
        val dir = cacheDir ?: return
        scope.launch(Dispatchers.IO) { dir.listFiles()?.forEach { it.delete() } }
    }

    fun clearCache() {
        refreshing.clear()
        categoryCache.clear()
        streamCache.clear()
        seriesCache.clear()
        searchNames.clear()
        epgCache.clear()
        flights.values.forEach { it.deferred.cancel() }
        flights.clear()
    }

    suspend fun categories(kind: Kind): List<Category> {
        val api = api()
        return cached(api, "cat|${kind.name}", { categoryCache[kind] }, { categoryCache[kind] = it },
            { api.rawCategories(kind) }, { api.parseCategories(it) })
    }

    suspend fun streams(kind: Kind, categoryId: String?, onFresh: ((List<MediaEntry>) -> Unit)? = null): List<MediaEntry> {
        val key = "${kind.name}|${categoryId ?: "*"}"
        val api = api()
        return cached(api, "streams|$key", { streamCache[key] }, { streamCache[key] = it },
            { api.rawStreams(kind, categoryId) }, { api.parseStreams(kind, it) }, onFresh)
    }

    /** Search-normalised names of a whole catalogue, computed once off the main thread. */
    suspend fun searchIndex(kind: Kind, all: List<MediaEntry>): Array<String> {
        searchNames[kind]?.let { (list, names) -> if (list === all) return names }
        val names = withContext(Dispatchers.Default) { Array(all.size) { normalizeForSearch(all[it].name) } }
        searchNames[kind] = all to names
        return names
    }

    suspend fun seriesInfo(seriesId: String): SeriesInfo {
        val api = api()
        return cached(api, "series|$seriesId", { seriesCache[seriesId] }, { seriesCache[seriesId] = it },
            { api.rawSeriesInfo(seriesId) }, { api.parseSeriesInfo(it) })
    }

    // Short EPG answers are reused for a minute so zapping back and forth does not re-ask the panel.
    private val epgCache = HashMap<String, Pair<Long, EpgNow?>>()

    suspend fun epg(streamId: String): EpgNow? {
        val hit = epgCache[streamId]
        val now = System.currentTimeMillis()
        if (hit != null && now - hit.first < 60_000) return hit.second
        val result = api().shortEpg(streamId)
        if (epgCache.size > 200) epgCache.clear()
        epgCache[streamId] = now to result
        return result
    }
}

/** Hand-off between screens for lists that are too large for Intent extras. */
object PlayerSession {
    var liveChannels: List<MediaEntry> = emptyList()
    var liveTitle: String = ""

    var seriesId: String = ""
    var seriesName: String = ""
    var seriesCover: String? = null
    var episodes: List<Episode> = emptyList()
}

fun normalizeForSearch(s: String): String {
    val t = s.lowercase(java.util.Locale("tr"))
    val b = StringBuilder(t.length)
    for (c in t) b.append(
        when (c) {
            'ı', 'î' -> 'i'; 'ğ' -> 'g'; 'ü' -> 'u'; 'ş' -> 's'; 'ö' -> 'o'; 'ç' -> 'c'; 'â' -> 'a'
            else -> c
        }
    )
    return b.toString()
}
