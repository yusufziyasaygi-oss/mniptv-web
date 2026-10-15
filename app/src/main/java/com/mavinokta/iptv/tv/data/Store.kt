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

    fun favorites(): List<MediaEntry> = readList(KEY_FAV).mapNotNull { MediaEntry.fromJson(it) }

    fun isFavorite(key: String): Boolean = favorites().any { it.key == key }

    fun favoriteKeys(): Set<String> = favorites().mapTo(HashSet()) { it.key }

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
        writeList(KEY_FAV, list.map { it.toJson() })
        return added
    }

    // ---- continue watching ----

    fun progressList(): List<Progress> =
        readList(KEY_PROGRESS).mapNotNull { Progress.fromJson(it) }.sortedByDescending { it.updatedAt }

    fun progress(key: String): Progress? = progressList().firstOrNull { it.key == key }

    fun saveProgress(p: Progress) {
        val list = progressList().filter { it.key != p.key }.toMutableList()
        list.add(0, p)
        writeList(KEY_PROGRESS, list.take(80).map { it.toJson() })
    }

    fun removeProgress(key: String) {
        writeList(KEY_PROGRESS, progressList().filter { it.key != key }.map { it.toJson() })
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
    }
}
