package com.mavinokta.iptv.tv.data

import org.json.JSONObject

enum class Kind { LIVE, MOVIE, SERIES }

data class Category(val id: String, val name: String)

data class Account(val server: String, val username: String, val password: String) {
    companion object {
        fun normalizeServer(input: String): String {
            var s = input.trim()
            if (s.isEmpty()) return s
            if (!s.startsWith("http://", ignoreCase = true) && !s.startsWith("https://", ignoreCase = true)) {
                s = "http://$s"
            }
            s = s.substringBefore("/player_api.php").substringBefore("/get.php")
            while (s.endsWith("/")) s = s.dropLast(1)
            return s
        }
    }
}

/** A channel, movie or series as listed in the catalogue. */
data class MediaEntry(
    val kind: Kind,
    val id: String,
    val name: String,
    val icon: String?,
    val ext: String?,
    val categoryId: String?,
    val number: Int = 0
) {
    val key: String get() = "${kind.name}:$id"

    fun toJson(): JSONObject = JSONObject().apply {
        put("kind", kind.name)
        put("id", id)
        put("name", name)
        put("icon", icon ?: "")
        put("ext", ext ?: "")
        put("cat", categoryId ?: "")
        put("num", number)
    }

    companion object {
        fun fromJson(o: JSONObject): MediaEntry? {
            val kind = runCatching { Kind.valueOf(o.optString("kind")) }.getOrNull() ?: return null
            val id = o.str("id") ?: return null
            return MediaEntry(
                kind = kind,
                id = id,
                name = o.str("name") ?: id,
                icon = o.str("icon"),
                ext = o.str("ext"),
                categoryId = o.str("cat"),
                number = o.int("num")
            )
        }
    }
}

data class Episode(
    val id: String,
    val season: Int,
    val number: Int,
    val title: String,
    val ext: String,
    val image: String?,
    val durationSec: Long
)

data class SeriesInfo(
    val name: String,
    val plot: String?,
    val cover: String?,
    val episodesBySeason: Map<Int, List<Episode>>
) {
    val seasons: List<Int> get() = episodesBySeason.keys.sorted()
    val allEpisodes: List<Episode>
        get() = seasons.flatMap { s -> episodesBySeason[s].orEmpty() }
}

data class EpgNow(
    val title: String,
    val start: Long,
    val end: Long,
    val nextTitle: String? = null,
    val nextStart: Long = 0L
)

/**
 * One "continue watching" card. Movies are keyed per movie, series are keyed per
 * series so a series only ever has one card (pointing at its last/next episode).
 */
data class Progress(
    val key: String,
    val kind: Kind,
    val id: String,
    val title: String,
    val subtitle: String?,
    val image: String?,
    val ext: String?,
    val episodeId: String?,
    val season: Int,
    val episode: Int,
    val positionMs: Long,
    val durationMs: Long,
    val updatedAt: Long
) {
    val fraction: Float
        get() = if (durationMs > 0) (positionMs.toFloat() / durationMs).coerceIn(0f, 1f) else 0f

    fun toJson(): JSONObject = JSONObject().apply {
        put("key", key)
        put("kind", kind.name)
        put("id", id)
        put("title", title)
        put("subtitle", subtitle ?: "")
        put("image", image ?: "")
        put("ext", ext ?: "")
        put("episodeId", episodeId ?: "")
        put("season", season)
        put("episode", episode)
        put("pos", positionMs)
        put("dur", durationMs)
        put("at", updatedAt)
    }

    companion object {
        fun fromJson(o: JSONObject): Progress? {
            val kind = runCatching { Kind.valueOf(o.optString("kind")) }.getOrNull() ?: return null
            val key = o.str("key") ?: return null
            val id = o.str("id") ?: return null
            return Progress(
                key = key,
                kind = kind,
                id = id,
                title = o.str("title") ?: "",
                subtitle = o.str("subtitle"),
                image = o.str("image"),
                ext = o.str("ext"),
                episodeId = o.str("episodeId"),
                season = o.int("season"),
                episode = o.int("episode"),
                positionMs = o.long("pos"),
                durationMs = o.long("dur"),
                updatedAt = o.long("at")
            )
        }
    }
}

/** Lenient JSON helpers: Xtream panels mix numbers, strings, "" and null for the same field. */
internal fun JSONObject.str(name: String): String? {
    if (!has(name) || isNull(name)) return null
    val s = opt(name)?.toString()?.trim() ?: return null
    return if (s.isEmpty() || s == "null") null else s
}

internal fun JSONObject.int(name: String, def: Int = 0): Int =
    str(name)?.toDoubleOrNull()?.toInt() ?: def

internal fun JSONObject.long(name: String, def: Long = 0L): Long =
    str(name)?.toDoubleOrNull()?.toLong() ?: def
