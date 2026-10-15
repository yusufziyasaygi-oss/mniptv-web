package com.mavinokta.iptv.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.mavinokta.iptv.tv.R
import com.mavinokta.iptv.tv.data.Episode
import com.mavinokta.iptv.tv.data.Kind
import com.mavinokta.iptv.tv.data.MediaEntry
import com.mavinokta.iptv.tv.data.PlayerSession
import com.mavinokta.iptv.tv.data.Repo
import com.mavinokta.iptv.tv.data.SeriesInfo
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

class SeriesActivity : AppCompatActivity() {

    companion object {
        private const val EXTRA_ID = "id"
        private const val EXTRA_NAME = "name"
        private const val EXTRA_COVER = "cover"

        fun intent(context: Context, seriesId: String, name: String, cover: String?): Intent =
            Intent(context, SeriesActivity::class.java)
                .putExtra(EXTRA_ID, seriesId)
                .putExtra(EXTRA_NAME, name)
                .putExtra(EXTRA_COVER, cover)
    }

    private lateinit var titleView: TextView
    private lateinit var plot: TextView
    private lateinit var poster: ImageView
    private lateinit var backdrop: ImageView
    private lateinit var resumeButton: TextView
    private lateinit var favButton: TextView
    private lateinit var seasonList: RecyclerView
    private lateinit var episodeList: RecyclerView
    private lateinit var loading: ProgressBar
    private lateinit var errorText: TextView

    private lateinit var seasonAdapter: TextListAdapter
    private lateinit var episodeAdapter: EpisodeAdapter

    private val handler = Handler(Looper.getMainLooper())
    private var seriesId = ""
    private var seriesName = ""
    private var cover: String? = null
    private var info: SeriesInfo? = null
    private var seasons: List<Int> = emptyList()
    private var selectedSeason = -1
    private var pendingSeason = -1

    private val store get() = Repo.store

    private val seasonRunnable = Runnable {
        if (pendingSeason != selectedSeason) selectSeason(pendingSeason)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Repo.init(this)
        setContentView(R.layout.activity_series)

        seriesId = intent.getStringExtra(EXTRA_ID).orEmpty()
        seriesName = intent.getStringExtra(EXTRA_NAME).orEmpty()
        cover = intent.getStringExtra(EXTRA_COVER)
        if (seriesId.isEmpty()) {
            finish()
            return
        }

        titleView = findViewById(R.id.title)
        plot = findViewById(R.id.plot)
        poster = findViewById(R.id.poster)
        backdrop = findViewById(R.id.backdrop)
        resumeButton = findViewById(R.id.resumeButton)
        favButton = findViewById(R.id.favButton)
        seasonList = findViewById(R.id.seasonList)
        episodeList = findViewById(R.id.episodeList)
        loading = findViewById(R.id.loading)
        errorText = findViewById(R.id.errorText)

        titleView.text = seriesName
        poster.loadPoster(cover)
        backdrop.loadPoster(cover)

        seasonAdapter = TextListAdapter(
            R.layout.item_season,
            onFocus = { i ->
                handler.removeCallbacks(seasonRunnable)
                pendingSeason = i
                handler.postDelayed(seasonRunnable, 250)
            },
            onClick = { i ->
                handler.removeCallbacks(seasonRunnable)
                if (i != selectedSeason) selectSeason(i)
                episodeList.focusRow(0)
            }
        )
        seasonList.layoutManager = LinearLayoutManager(this, LinearLayoutManager.HORIZONTAL, false)
        seasonList.itemAnimator = null
        seasonList.adapter = seasonAdapter

        episodeAdapter = EpisodeAdapter { ep -> onEpisodeClick(ep) }
        episodeList.layoutManager = LinearLayoutManager(this)
        episodeList.itemAnimator = null
        episodeList.adapter = episodeAdapter

        favButton.setOnClickListener {
            val added = store.toggleFavorite(MediaEntry(Kind.SERIES, seriesId, seriesName, cover, null, null))
            Toast.makeText(this, if (added) "Favorilere eklendi" else "Favorilerden çıkarıldı", Toast.LENGTH_SHORT).show()
            updateFavButton()
        }
        updateFavButton()
        favButton.requestFocus()

        load()
    }

    override fun onResume() {
        super.onResume()
        if (info != null) {
            updateResume()
            episodeAdapter.notifyItemRangeChanged(0, episodeAdapter.itemCount)
        }
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    private fun updateFavButton() {
        favButton.text = if (store.isFavorite("SERIES:$seriesId")) "Favorilerden çıkar" else "Favorilere ekle"
    }

    private fun load() {
        loading.visibility = View.VISIBLE
        errorText.visibility = View.GONE
        lifecycleScope.launch {
            try {
                val i = Repo.seriesInfo(seriesId)
                info = i
                if (seriesName.isBlank() && i.name.isNotBlank()) {
                    seriesName = i.name
                    titleView.text = seriesName
                }
                if (cover.isNullOrBlank() && !i.cover.isNullOrBlank()) {
                    cover = i.cover
                    poster.loadPoster(cover)
                    backdrop.loadPoster(cover)
                }
                plot.text = i.plot.orEmpty()
                seasons = i.seasons
                loading.visibility = View.GONE
                if (seasons.isEmpty()) {
                    errorText.text = "Bu dizide bölüm bulunamadı"
                    errorText.visibility = View.VISIBLE
                    return@launch
                }
                val progress = store.progress("SERIES:$seriesId")
                val startSeason = progress?.let { p -> seasons.indexOf(p.season).takeIf { it >= 0 } } ?: 0
                seasonAdapter.submit(seasons.map { "Sezon $it" }, startSeason)
                selectSeason(startSeason)
                updateResume()
                if (resumeButton.visibility == View.VISIBLE) resumeButton.requestFocus()
                else episodeList.post { episodeList.focusRow(0) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                loading.visibility = View.GONE
                errorText.text = "Dizi bilgisi alınamadı: ${e.message ?: "bağlantı hatası"}"
                errorText.visibility = View.VISIBLE
            }
        }
    }

    private fun selectSeason(index: Int) {
        val season = seasons.getOrNull(index) ?: return
        selectedSeason = index
        seasonAdapter.setActivated(index)
        episodeAdapter.submit(info?.episodesBySeason?.get(season).orEmpty())
        episodeList.scrollToPosition(0)
    }

    private fun updateResume() {
        val all = info?.allEpisodes.orEmpty()
        val p = store.progress("SERIES:$seriesId")
        val ep = p?.episodeId?.let { id -> all.firstOrNull { it.id == id } }
        if (p == null || ep == null) {
            resumeButton.visibility = View.GONE
            return
        }
        resumeButton.visibility = View.VISIBLE
        resumeButton.text = if (p.positionMs > 0) {
            "Devam et · S${ep.season} B${ep.number} · ${formatTime(p.positionMs)}"
        } else {
            "Sıradaki · S${ep.season} B${ep.number}"
        }
        resumeButton.setOnClickListener { play(ep, p.positionMs) }
    }

    private fun onEpisodeClick(ep: Episode) {
        val saved = store.episodePosition(ep.id)
        if (saved > 30_000 && !store.isWatched(ep.id)) {
            AlertDialog.Builder(this)
                .setTitle("S${ep.season} B${ep.number} · ${ep.title}")
                .setItems(arrayOf("Kaldığın yerden devam et (${formatTime(saved)})", "Baştan başla")) { _, which ->
                    play(ep, if (which == 0) saved else 0L)
                }
                .show()
        } else {
            play(ep, 0L)
        }
    }

    private fun play(ep: Episode, startMs: Long) {
        val all = info?.allEpisodes.orEmpty()
        val index = all.indexOfFirst { it.id == ep.id }
        if (index < 0) return
        PlayerSession.seriesId = seriesId
        PlayerSession.seriesName = seriesName
        PlayerSession.seriesCover = cover
        PlayerSession.episodes = all
        startActivity(PlayerActivity.series(this, index, startMs))
    }
}
