package com.mavinokta.iptv.tv.data

import android.net.Uri
import android.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.URLEncoder

const val USER_AGENT = "MN-IPTV-TV/2.1.0 (Android TV)"

class XtreamApi(val account: Account, private val http: OkHttpClient) {

    private fun q(s: String): String = URLEncoder.encode(s, "UTF-8")
    private fun p(s: String): String = Uri.encode(s)

    private fun apiUrl(action: String?, extra: String = ""): String = buildString {
        append(account.server)
        append("/player_api.php?username=").append(q(account.username))
        append("&password=").append(q(account.password))
        if (action != null) append("&action=").append(action)
        append(extra)
    }

    private suspend fun get(url: String): String = withContext(Dispatchers.IO) {
        val request = try {
            Request.Builder().url(url).header("User-Agent", USER_AGENT).build()
        } catch (e: IllegalArgumentException) {
            throw IOException("Sunucu adresi geçersiz")
        }
        http.newCall(request).execute().use { response ->
            if (!response.isSuccessful) throw IOException("Sunucu yanıtı: HTTP ${response.code}")
            response.body?.string() ?: ""
        }
    }

    private fun array(body: String): JSONArray {
        val t = body.trim()
        return if (t.startsWith("[")) JSONArray(t) else JSONArray()
    }

    /** Returns null when login is OK, otherwise a user-facing error message. */
    suspend fun authenticate(): String? {
        val body = get(apiUrl(null)).trim()
        if (!body.startsWith("{")) return "Bu adres bir Xtream sunucusu gibi görünmüyor"
        val root = withContext(Dispatchers.Default) { JSONObject(body) }
        val info = root.optJSONObject("user_info") ?: return "Sunucu yanıtı tanınmadı"
        if (info.int("auth") != 1) return "Kullanıcı adı veya şifre hatalı"
        val status = info.str("status")
        if (status != null && !status.equals("Active", ignoreCase = true)) {
            return "Hesap durumu: $status"
        }
        return null
    }

    suspend fun categories(kind: Kind): List<Category> {
        val action = when (kind) {
            Kind.LIVE -> "get_live_categories"
            Kind.MOVIE -> "get_vod_categories"
            Kind.SERIES -> "get_series_categories"
        }
        val body = get(apiUrl(action))
        return withContext(Dispatchers.Default) {
            val a = array(body)
            (0 until a.length()).mapNotNull { i ->
                val o = a.optJSONObject(i) ?: return@mapNotNull null
                val id = o.str("category_id") ?: return@mapNotNull null
                Category(id, o.str("category_name") ?: id)
            }
        }
    }

    /** categoryId == null loads the whole catalogue of that kind (used for search). */
    suspend fun streams(kind: Kind, categoryId: String?): List<MediaEntry> {
        val action = when (kind) {
            Kind.LIVE -> "get_live_streams"
            Kind.MOVIE -> "get_vod_streams"
            Kind.SERIES -> "get_series"
        }
        val extra = if (categoryId.isNullOrEmpty()) "" else "&category_id=" + q(categoryId)
        val body = get(apiUrl(action, extra))
        return withContext(Dispatchers.Default) {
            val a = array(body)
            val out = ArrayList<MediaEntry>(a.length())
            for (i in 0 until a.length()) {
                val o = a.optJSONObject(i) ?: continue
                val entry = when (kind) {
                    Kind.LIVE -> o.str("stream_id")?.let { id ->
                        MediaEntry(Kind.LIVE, id, o.str("name") ?: id, o.str("stream_icon"), null,
                            o.str("category_id"), o.int("num"))
                    }
                    Kind.MOVIE -> o.str("stream_id")?.let { id ->
                        MediaEntry(Kind.MOVIE, id, o.str("name") ?: id, o.str("stream_icon"),
                            o.str("container_extension") ?: "mp4", o.str("category_id"))
                    }
                    Kind.SERIES -> o.str("series_id")?.let { id ->
                        MediaEntry(Kind.SERIES, id, o.str("name") ?: id, o.str("cover"), null,
                            o.str("category_id"))
                    }
                }
                if (entry != null) out.add(entry)
            }
            if (kind == Kind.LIVE && out.any { it.number > 0 }) {
                out.sortWith(compareBy { if (it.number > 0) it.number else Int.MAX_VALUE })
            }
            out
        }
    }

    suspend fun seriesInfo(seriesId: String): SeriesInfo {
        val body = get(apiUrl("get_series_info", "&series_id=" + q(seriesId))).trim()
        return withContext(Dispatchers.Default) {
            if (!body.startsWith("{")) throw IOException("Dizi bilgisi alınamadı")
            val root = JSONObject(body)
            val info = root.optJSONObject("info")
            val bySeason = LinkedHashMap<Int, MutableList<Episode>>()

            fun addEpisode(o: JSONObject, seasonHint: Int) {
                val id = o.str("id") ?: return
                val season = o.int("season", seasonHint).let { if (it <= 0) seasonHint else it }
                val epInfo = o.optJSONObject("info")
                val ep = Episode(
                    id = id,
                    season = season,
                    number = o.int("episode_num"),
                    title = o.str("title") ?: "Bölüm ${o.int("episode_num")}",
                    ext = o.str("container_extension") ?: "mp4",
                    image = epInfo?.str("movie_image"),
                    durationSec = epInfo?.long("duration_secs") ?: 0L
                )
                bySeason.getOrPut(season) { mutableListOf() }.add(ep)
            }

            when (val eps = root.opt("episodes")) {
                is JSONObject -> {
                    val keys = eps.keys()
                    while (keys.hasNext()) {
                        val k = keys.next()
                        val arr = eps.optJSONArray(k) ?: continue
                        val hint = k.toIntOrNull() ?: 1
                        for (i in 0 until arr.length()) arr.optJSONObject(i)?.let { addEpisode(it, hint) }
                    }
                }
                is JSONArray -> {
                    for (i in 0 until eps.length()) {
                        when (val item = eps.opt(i)) {
                            is JSONArray -> for (j in 0 until item.length()) {
                                item.optJSONObject(j)?.let { addEpisode(it, i + 1) }
                            }
                            is JSONObject -> addEpisode(item, 1)
                        }
                    }
                }
            }
            val sorted = bySeason.mapValues { (_, v) -> v.sortedBy { it.number } }
            SeriesInfo(
                name = info?.str("name") ?: "",
                plot = info?.str("plot"),
                cover = info?.str("cover"),
                episodesBySeason = sorted
            )
        }
    }

    suspend fun shortEpg(streamId: String): EpgNow? {
        val body = get(apiUrl("get_short_epg", "&stream_id=" + q(streamId) + "&limit=2")).trim()
        return withContext(Dispatchers.Default) {
            if (!body.startsWith("{")) return@withContext null
            val list = JSONObject(body).optJSONArray("epg_listings") ?: return@withContext null
            val now = System.currentTimeMillis() / 1000
            for (i in 0 until list.length()) {
                val o = list.optJSONObject(i) ?: continue
                val start = o.long("start_timestamp")
                val end = o.long("stop_timestamp")
                if (start <= now && now < end) {
                    return@withContext EpgNow(decode(o.str("title")), start, end)
                }
            }
            null
        }
    }

    private fun decode(b64: String?): String {
        if (b64.isNullOrEmpty()) return ""
        return try {
            String(Base64.decode(b64, Base64.DEFAULT), Charsets.UTF_8).trim()
        } catch (e: IllegalArgumentException) {
            b64
        }
    }

    fun liveUrl(streamId: String, ext: String): String =
        "${account.server}/live/${p(account.username)}/${p(account.password)}/$streamId.$ext"

    fun movieUrl(streamId: String, ext: String): String =
        "${account.server}/movie/${p(account.username)}/${p(account.password)}/$streamId.$ext"

    fun episodeUrl(episodeId: String, ext: String): String =
        "${account.server}/series/${p(account.username)}/${p(account.password)}/$episodeId.$ext"
}
