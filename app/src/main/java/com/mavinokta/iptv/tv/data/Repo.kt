package com.mavinokta.iptv.tv.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import java.io.IOException
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

    fun init(context: Context) {
        if (!::store.isInitialized) store = Store(context)
    }

    fun api(): XtreamApi {
        val account = store.account ?: throw IOException("Hesap bilgisi yok")
        val current = cachedApi
        if (current != null && current.account == account) return current
        clearCache()
        return XtreamApi(account, http).also { cachedApi = it }
    }

    fun clearCache() {
        categoryCache.clear()
        streamCache.clear()
        seriesCache.clear()
        searchNames.clear()
        flights.values.forEach { it.deferred.cancel() }
        flights.clear()
    }

    suspend fun categories(kind: Kind): List<Category> {
        categoryCache[kind]?.let { return it }
        val api = api()
        val list = shared("cat|${kind.name}") { api.categories(kind) }
        if (cachedApi === api) categoryCache[kind] = list
        return list
    }

    suspend fun streams(kind: Kind, categoryId: String?): List<MediaEntry> {
        val key = "${kind.name}|${categoryId ?: "*"}"
        streamCache[key]?.let { return it }
        val api = api()
        val list = shared("streams|$key") { api.streams(kind, categoryId) }
        if (cachedApi === api) streamCache[key] = list
        return list
    }

    /** Search-normalised names of a whole catalogue, computed once off the main thread. */
    suspend fun searchIndex(kind: Kind, all: List<MediaEntry>): Array<String> {
        searchNames[kind]?.let { (list, names) -> if (list === all) return names }
        val names = withContext(Dispatchers.Default) { Array(all.size) { normalizeForSearch(all[it].name) } }
        searchNames[kind] = all to names
        return names
    }

    suspend fun seriesInfo(seriesId: String): SeriesInfo {
        seriesCache[seriesId]?.let { return it }
        val api = api()
        val info = shared("series|$seriesId") { api.seriesInfo(seriesId) }
        if (cachedApi === api) seriesCache[seriesId] = info
        return info
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
