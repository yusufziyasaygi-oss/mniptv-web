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
    }

    fun removeProgress(key: String) {
        val kept = progressList().filter { it.key != key }
        if (kept.size == progressList().size) return
        progressCache = kept
        writeList(KEY_PROGRESS, kept.map { it.toJson() })
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
    }

    fun isWatched(episodeId: String): Boolean = prefs.getBoolean("ew_$episodeId", false)

    fun setWatched(episodeId: String) {
        prefs.edit().putBoolean("ew_$episodeId", true).putLong("epos_$episodeId", 0L).apply()
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
    }
}
