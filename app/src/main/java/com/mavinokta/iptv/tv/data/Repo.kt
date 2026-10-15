package com.mavinokta.iptv.tv.data

import android.content.Context
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
            .readTimeout(30, TimeUnit.SECONDS)
            .followRedirects(true)
            .followSslRedirects(true)
            .retryOnConnectionFailure(true)
            .build()
    }

    private var cachedApi: XtreamApi? = null
    private val categoryCache = HashMap<Kind, List<Category>>()
    private val streamCache = HashMap<String, List<MediaEntry>>()
    private val seriesCache = HashMap<String, SeriesInfo>()

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
    }

    suspend fun categories(kind: Kind): List<Category> {
        categoryCache[kind]?.let { return it }
        val list = api().categories(kind)
        categoryCache[kind] = list
        return list
    }

    suspend fun streams(kind: Kind, categoryId: String?): List<MediaEntry> {
        val key = "${kind.name}|${categoryId ?: "*"}"
        streamCache[key]?.let { return it }
        val list = api().streams(kind, categoryId)
        streamCache[key] = list
        return list
    }

    suspend fun seriesInfo(seriesId: String): SeriesInfo {
        seriesCache[seriesId]?.let { return it }
        val info = api().seriesInfo(seriesId)
        seriesCache[seriesId] = info
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
