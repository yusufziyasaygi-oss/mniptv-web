package com.mavinokta.iptv.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.KeyEvent
import android.view.View
import android.view.WindowManager
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.activity.OnBackPressedCallback
import androidx.annotation.OptIn
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.Format
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.TrackSelectionOverride
import androidx.media3.common.Tracks
import androidx.media3.common.VideoSize
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.DefaultRenderersFactory
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.extractor.DefaultExtractorsFactory
import androidx.media3.extractor.ts.DefaultTsPayloadReaderFactory
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.DefaultTimeBar
import androidx.media3.ui.PlayerView
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.mavinokta.iptv.tv.R
import com.mavinokta.iptv.tv.data.EpgNow
import com.mavinokta.iptv.tv.data.Episode
import com.mavinokta.iptv.tv.data.Kind
import com.mavinokta.iptv.tv.data.MediaEntry
import com.mavinokta.iptv.tv.data.PlayerSession
import com.mavinokta.iptv.tv.data.Progress
import com.mavinokta.iptv.tv.data.Repo
import com.mavinokta.iptv.tv.data.USER_AGENT
import com.mavinokta.iptv.tv.data.XtreamApi
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(UnstableApi::class)
class PlayerActivity : AppCompatActivity() {

    companion object {
        private const val EXTRA_MODE = "mode"
        private const val EXTRA_INDEX = "index"
        private const val EXTRA_POS = "pos"
        private const val EXTRA_ID = "id"
        private const val EXTRA_NAME = "name"
        private const val EXTRA_EXT = "ext"
        private const val EXTRA_ICON = "icon"

        private const val MODE_LIVE = 0
        private const val MODE_MOVIE = 1
        private const val MODE_SERIES = 2

        fun live(context: Context, index: Int): Intent =
            Intent(context, PlayerActivity::class.java)
                .putExtra(EXTRA_MODE, MODE_LIVE)
                .putExtra(EXTRA_INDEX, index)

        fun movie(context: Context, entry: MediaEntry, startMs: Long): Intent =
            Intent(context, PlayerActivity::class.java)
                .putExtra(EXTRA_MODE, MODE_MOVIE)
                .putExtra(EXTRA_ID, entry.id)
                .putExtra(EXTRA_NAME, entry.name)
                .putExtra(EXTRA_EXT, entry.ext ?: "mp4")
                .putExtra(EXTRA_ICON, entry.icon)
                .putExtra(EXTRA_POS, startMs)

        fun series(context: Context, index: Int, startMs: Long): Intent =
            Intent(context, PlayerActivity::class.java)
                .putExtra(EXTRA_MODE, MODE_SERIES)
                .putExtra(EXTRA_INDEX, index)
                .putExtra(EXTRA_POS, startMs)
    }

    private lateinit var playerView: PlayerView
    private lateinit var titleBar: View
    private lateinit var titleText: TextView
    private lateinit var subtitleText: TextView
    private lateinit var infoBar: View
    private lateinit var infoLogo: ImageView
    private lateinit var infoNumber: TextView
    private lateinit var infoName: TextView
    private lateinit var infoFav: TextView
    private lateinit var infoBadge: TextView
    private lateinit var infoEpg: TextView
    private lateinit var infoEpgProgress: ProgressBar
    private lateinit var channelPanel: View
    private lateinit var panelTitle: TextView
    private lateinit var channelList: RecyclerView
    private lateinit var numberOverlay: TextView
    private lateinit var seekOverlay: TextView
    private lateinit var messageText: TextView

    private lateinit var player: ExoPlayer
    private lateinit var api: XtreamApi
    private lateinit var channelAdapter: ChannelRowAdapter

    private val handler = Handler(Looper.getMainLooper())
    private val store get() = Repo.store
    private var mode = MODE_LIVE

    // live
    private var channels: List<MediaEntry> = emptyList()
    private var current = -1
    private var playing = -1
    private var previous = -1
    private var liveExt = "ts"
    private var liveRetries = 0
    private var epg: EpgNow? = null
    private var epgJob: Job? = null
    private val numberBuffer = StringBuilder()

    // movie / series
    private var movieId = ""
    private var movieName = ""
    private var movieExt = "mp4"
    private var movieIcon: String? = null
    private var episodes: List<Episode> = emptyList()
    private var lastEpisodeIndex = -1
    private var vodRetries = 0
    private var seekTarget = -1L

    // keys / lifecycle
    private val consumedKeys = HashSet<Int>()
    private var centerLongHandled = false
    private var stoppedInBackground = false
    private var resizeIndex = 0
    private val resizeModes = intArrayOf(
        AspectRatioFrameLayout.RESIZE_MODE_FIT,
        AspectRatioFrameLayout.RESIZE_MODE_FILL,
        AspectRatioFrameLayout.RESIZE_MODE_ZOOM
    )
    private val resizeNames = arrayOf("Sığdır", "Doldur", "Yakınlaştır")

    private val hideInfoRunnable = Runnable { infoBar.visibility = View.GONE }
    private val hideSeekRunnable = Runnable {
        seekOverlay.visibility = View.GONE
        seekTarget = -1L
    }
    private val tuneRunnable = Runnable { startLive() }
    private val numberRunnable = Runnable { commitNumber() }
    private val saveRunnable = object : Runnable {
        override fun run() {
            saveProgress()
            handler.postDelayed(this, 10_000)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Repo.init(this)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        mode = intent.getIntExtra(EXTRA_MODE, MODE_LIVE)
        api = try {
            Repo.api()
        } catch (e: Exception) {
            finish()
            return
        }
        when (mode) {
            MODE_LIVE -> {
                channels = PlayerSession.liveChannels
                if (channels.isEmpty()) {
                    finish()
                    return
                }
            }
            MODE_SERIES -> {
                episodes = PlayerSession.episodes
                if (episodes.isEmpty()) {
                    finish()
                    return
                }
            }
            else -> {
                movieId = intent.getStringExtra(EXTRA_ID).orEmpty()
                movieName = intent.getStringExtra(EXTRA_NAME).orEmpty()
                movieExt = intent.getStringExtra(EXTRA_EXT) ?: "mp4"
                movieIcon = intent.getStringExtra(EXTRA_ICON)
                if (movieId.isEmpty()) {
                    finish()
                    return
                }
            }
        }

        setContentView(R.layout.activity_player)
        bindViews()
        buildPlayer()

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = onBack()
        })

        when (mode) {
            MODE_LIVE -> {
                playerView.useController = false
                setupChannelPanel()
                tune(intent.getIntExtra(EXTRA_INDEX, 0).coerceIn(0, channels.size - 1), immediate = true)
            }
            MODE_MOVIE -> {
                setupVodUi()
                val item = MediaItem.Builder().setUri(api.movieUrl(movieId, movieExt)).setMediaId(movieId).build()
                player.setMediaItem(item, intent.getLongExtra(EXTRA_POS, 0L).coerceAtLeast(0L))
                player.prepare()
                player.playWhenReady = true
                updateTitle()
                handler.postDelayed(saveRunnable, 10_000)
            }
            MODE_SERIES -> {
                setupVodUi()
                val start = intent.getIntExtra(EXTRA_INDEX, 0).coerceIn(0, episodes.size - 1)
                val items = episodes.map {
                    MediaItem.Builder().setUri(api.episodeUrl(it.id, it.ext)).setMediaId(it.id).build()
                }
                player.setMediaItems(items, start, intent.getLongExtra(EXTRA_POS, 0L).coerceAtLeast(0L))
                player.prepare()
                player.playWhenReady = true
                lastEpisodeIndex = start
                updateTitle()
                handler.postDelayed(saveRunnable, 10_000)
            }
        }
    }

    private fun bindViews() {
        playerView = findViewById(R.id.playerView)
        titleBar = findViewById(R.id.titleBar)
        titleText = findViewById(R.id.titleText)
        subtitleText = findViewById(R.id.subtitleText)
        infoBar = findViewById(R.id.infoBar)
        infoLogo = findViewById(R.id.infoLogo)
        infoNumber = findViewById(R.id.infoNumber)
        infoName = findViewById(R.id.infoName)
        infoFav = findViewById(R.id.infoFav)
        infoBadge = findViewById(R.id.infoBadge)
        infoEpg = findViewById(R.id.infoEpg)
        infoEpgProgress = findViewById(R.id.infoEpgProgress)
        channelPanel = findViewById(R.id.channelPanel)
        panelTitle = findViewById(R.id.panelTitle)
        channelList = findViewById(R.id.channelList)
        numberOverlay = findViewById(R.id.numberOverlay)
        seekOverlay = findViewById(R.id.seekOverlay)
        messageText = findViewById(R.id.messageText)
    }

    private fun buildPlayer() {
        val dataSourceFactory = OkHttpDataSource.Factory(Repo.http).setUserAgent(USER_AGENT)
        val extractors = DefaultExtractorsFactory()
            .setTsExtractorFlags(
                DefaultTsPayloadReaderFactory.FLAG_ALLOW_NON_IDR_KEYFRAMES or
                    DefaultTsPayloadReaderFactory.FLAG_DETECT_ACCESS_UNITS
            )
            .setConstantBitrateSeekingEnabled(true)
        val mediaSourceFactory = DefaultMediaSourceFactory(dataSourceFactory, extractors)
        val renderers = DefaultRenderersFactory(this).setEnableDecoderFallback(true)
        val loadControl = if (mode == MODE_LIVE) {
            DefaultLoadControl.Builder().setBufferDurationsMs(4_000, 30_000, 1_500, 3_000).build()
        } else {
            DefaultLoadControl.Builder().setBufferDurationsMs(15_000, 60_000, 2_000, 4_000).build()
        }
        player = ExoPlayer.Builder(this, renderers)
            .setMediaSourceFactory(mediaSourceFactory)
            .setLoadControl(loadControl)
            .setSeekBackIncrementMs(10_000)
            .setSeekForwardIncrementMs(10_000)
            .setAudioAttributes(
                AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(),
                true
            )
            .setHandleAudioBecomingNoisy(true)
            .build()
        player.addListener(playerListener)
        playerView.player = player
        playerView.setShowBuffering(PlayerView.SHOW_BUFFERING_ALWAYS)
        playerView.resizeMode = resizeModes[resizeIndex]
    }

    private fun setupVodUi() {
        playerView.useController = true
        playerView.controllerShowTimeoutMs = 4_000
        playerView.setShowSubtitleButton(true)
        playerView.setShowNextButton(mode == MODE_SERIES)
        playerView.setShowPreviousButton(mode == MODE_SERIES)
        playerView.setControllerVisibilityListener(PlayerView.ControllerVisibilityListener { visibility ->
            titleBar.visibility = visibility
        })
        playerView.findViewById<DefaultTimeBar>(androidx.media3.ui.R.id.exo_progress)?.setKeyTimeIncrement(10_000)
    }

    // ---------------------------------------------------------------- player events

    private val playerListener = object : Player.Listener {
        override fun onPlayerError(error: PlaybackException) = handleError(error)

        override fun onPlaybackStateChanged(playbackState: Int) {
            when (playbackState) {
                Player.STATE_READY -> {
                    messageText.visibility = View.GONE
                    vodRetries = 0
                    if (mode == MODE_LIVE) {
                        liveRetries = 0
                        updateBadge()
                    }
                }
                Player.STATE_ENDED -> when (mode) {
                    MODE_MOVIE -> {
                        store.removeProgress("MOVIE:$movieId")
                        finish()
                    }
                    MODE_SERIES -> {
                        saveProgress(finished = true)
                        finish()
                    }
                    else -> Unit
                }
                else -> Unit
            }
        }

        override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
            if (mode != MODE_SERIES) return
            val newIndex = player.currentMediaItemIndex
            if (reason == Player.MEDIA_ITEM_TRANSITION_REASON_AUTO && lastEpisodeIndex in episodes.indices) {
                store.setWatched(episodes[lastEpisodeIndex].id)
            }
            lastEpisodeIndex = newIndex
            episodes.getOrNull(newIndex)?.let { ep ->
                store.saveProgress(seriesProgress(ep, store.episodePosition(ep.id), ep.durationSec * 1000))
            }
            updateTitle()
        }

        override fun onVideoSizeChanged(videoSize: VideoSize) {
            if (mode == MODE_LIVE) updateBadge()
        }
    }

    private fun describe(error: PlaybackException): String = when (error.errorCode) {
        PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_FAILED,
        PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT -> "Sunucuya ulaşılamadı."
        PlaybackException.ERROR_CODE_IO_BAD_HTTP_STATUS -> "Sunucu yayını vermedi (HTTP hata)."
        PlaybackException.ERROR_CODE_IO_FILE_NOT_FOUND -> "Yayın sunucuda bulunamadı."
        PlaybackException.ERROR_CODE_IO_CLEARTEXT_NOT_PERMITTED -> "Güvensiz bağlantı engellendi."
        PlaybackException.ERROR_CODE_PARSING_CONTAINER_MALFORMED,
        PlaybackException.ERROR_CODE_PARSING_MANIFEST_MALFORMED,
        PlaybackException.ERROR_CODE_PARSING_CONTAINER_UNSUPPORTED,
        PlaybackException.ERROR_CODE_PARSING_MANIFEST_UNSUPPORTED -> "Yayın biçimi okunamadı."
        PlaybackException.ERROR_CODE_DECODER_INIT_FAILED,
        PlaybackException.ERROR_CODE_DECODER_QUERY_FAILED,
        PlaybackException.ERROR_CODE_DECODING_FAILED,
        PlaybackException.ERROR_CODE_DECODING_FORMAT_EXCEEDS_CAPABILITIES,
        PlaybackException.ERROR_CODE_DECODING_FORMAT_UNSUPPORTED -> "Bu cihaz videonun formatını çözemiyor."
        PlaybackException.ERROR_CODE_AUDIO_TRACK_INIT_FAILED -> "Ses çıkışı başlatılamadı."
        else -> error.errorCodeName
    }

    private fun isNetworkError(error: PlaybackException): Boolean =
        error.errorCode in 2000..2999

    private fun handleError(error: PlaybackException) {
        if (mode == MODE_LIVE) {
            if (error.errorCode == PlaybackException.ERROR_CODE_BEHIND_LIVE_WINDOW) {
                player.seekToDefaultPosition()
                player.prepare()
                return
            }
            val ch = channels.getOrNull(current) ?: return
            if (liveExt == "ts") {
                liveExt = "m3u8"
                playLiveUrl(api.liveUrl(ch.id, "m3u8"))
                return
            }
            if (liveRetries < 2) {
                liveRetries++
                liveExt = "ts"
                showMessage("Bağlantı yeniden kuruluyor…")
                handler.postDelayed({
                    if (!isFinishing && channels.getOrNull(current)?.id == ch.id) playLiveUrl(api.liveUrl(ch.id, "ts"))
                }, 2_000)
                return
            }
            showMessage("Bu kanal açılamadı.\n${describe(error)}")
        } else {
            if (vodRetries < 2 && isNetworkError(error)) {
                vodRetries++
                showMessage("Bağlantı yeniden kuruluyor…")
                handler.postDelayed({ if (!isFinishing) player.prepare() }, 2_000)
                return
            }
            showMessage("Oynatılamadı.\n${describe(error)}")
        }
    }

    private fun showMessage(text: String) {
        messageText.text = text
        messageText.visibility = View.VISIBLE
    }

    // ---------------------------------------------------------------- live

    private fun setupChannelPanel() {
        channelAdapter = ChannelRowAdapter(
            onClick = { pos ->
                closePanel()
                if (pos != current) tune(pos, immediate = true) else showInfo()
            },
            onLongClick = { pos -> toggleFavorite(channels[pos]) }
        )
        channelAdapter.items = channels
        channelAdapter.favorites = store.favoriteKeys()
        channelList.layoutManager = LinearLayoutManager(this)
        channelList.itemAnimator = null
        channelList.adapter = channelAdapter
    }

    private fun tune(index: Int, immediate: Boolean) {
        if (index !in channels.indices) return
        current = index
        messageText.visibility = View.GONE
        epg = null
        showInfo()
        handler.removeCallbacks(tuneRunnable)
        if (immediate) startLive() else handler.postDelayed(tuneRunnable, 450)
    }

    private fun startLive() {
        handler.removeCallbacks(tuneRunnable)
        val ch = channels.getOrNull(current) ?: return
        if (playing != current) {
            if (playing >= 0) previous = playing
            playing = current
        }
        liveExt = "ts"
        liveRetries = 0
        playLiveUrl(api.liveUrl(ch.id, liveExt))
        loadEpg(ch)
    }

    private fun playLiveUrl(url: String) {
        val builder = MediaItem.Builder().setUri(url)
        if (url.endsWith(".m3u8")) builder.setMimeType(MimeTypes.APPLICATION_M3U8)
        player.setMediaItem(builder.build())
        player.prepare()
        player.playWhenReady = true
    }

    private fun zap(delta: Int, repeating: Boolean) {
        val n = channels.size
        if (n == 0) return
        val base = if (current >= 0) current else 0
        tune(((base + delta) % n + n) % n, immediate = !repeating)
    }

    private fun loadEpg(ch: MediaEntry) {
        epgJob?.cancel()
        epgJob = lifecycleScope.launch {
            delay(600)
            val result = try {
                api.shortEpg(ch.id)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                null
            }
            if (channels.getOrNull(current)?.id != ch.id) return@launch
            epg = result
            renderEpg()
        }
    }

    private fun renderEpg() {
        val e = epg
        if (e == null || e.title.isBlank()) {
            infoEpg.text = ""
            infoEpgProgress.visibility = View.GONE
            return
        }
        val fmt = SimpleDateFormat("HH:mm", Locale.getDefault())
        infoEpg.text = "${e.title}   ${fmt.format(Date(e.start * 1000))} – ${fmt.format(Date(e.end * 1000))}"
        val now = System.currentTimeMillis() / 1000
        val total = (e.end - e.start).coerceAtLeast(1)
        infoEpgProgress.progress = (((now - e.start).toFloat() / total) * 1000).toInt().coerceIn(0, 1000)
        infoEpgProgress.visibility = View.VISIBLE
    }

    private fun showInfo(autoHideMs: Long = 5_000) {
        val ch = channels.getOrNull(current) ?: return
        infoNumber.text = if (ch.number > 0) ch.number.toString() else (current + 1).toString()
        infoName.text = ch.name
        infoLogo.loadLogo(ch.icon)
        infoFav.visibility = if (store.isFavorite(ch.key)) View.VISIBLE else View.GONE
        updateBadge()
        renderEpg()
        infoBar.visibility = View.VISIBLE
        handler.removeCallbacks(hideInfoRunnable)
        handler.postDelayed(hideInfoRunnable, autoHideMs)
    }

    private fun hideInfo() {
        handler.removeCallbacks(hideInfoRunnable)
        infoBar.visibility = View.GONE
    }

    private fun updateBadge() {
        if (!::infoBadge.isInitialized) return
        val h = player.videoSize.height
        val label = when {
            h >= 2000 -> "4K"
            h >= 1000 -> "FHD"
            h >= 700 -> "HD"
            h > 0 -> "SD"
            else -> null
        }
        if (label == null || playing != current) {
            infoBadge.visibility = View.GONE
        } else {
            infoBadge.text = label
            infoBadge.visibility = View.VISIBLE
        }
    }

    private fun openPanel() {
        hideInfo()
        panelTitle.text = PlayerSession.liveTitle.ifBlank { "Kanallar" }
        channelAdapter.current = current
        channelAdapter.favorites = store.favoriteKeys()
        channelAdapter.notifyDataSetChanged()
        channelPanel.visibility = View.VISIBLE
        channelList.post { channelList.focusRow(current) }
    }

    private fun closePanel() {
        channelPanel.visibility = View.GONE
        playerView.requestFocus()
    }

    private fun toggleFavorite(ch: MediaEntry) {
        val added = store.toggleFavorite(ch)
        Toast.makeText(this, if (added) "Favorilere eklendi" else "Favorilerden çıkarıldı", Toast.LENGTH_SHORT).show()
        if (::channelAdapter.isInitialized) {
            channelAdapter.favorites = store.favoriteKeys()
            channelAdapter.notifyItemRangeChanged(0, channelAdapter.itemCount)
        }
        if (channels.getOrNull(current)?.key == ch.key) {
            infoFav.visibility = if (added) View.VISIBLE else View.GONE
        }
    }

    private fun appendDigit(d: Int) {
        if (numberBuffer.length >= 5) numberBuffer.clear()
        numberBuffer.append(d)
        numberOverlay.text = numberBuffer.toString()
        numberOverlay.visibility = View.VISIBLE
        handler.removeCallbacks(numberRunnable)
        handler.postDelayed(numberRunnable, 1_500)
    }

    private fun commitNumber() {
        numberOverlay.visibility = View.GONE
        val n = numberBuffer.toString().toIntOrNull()
        numberBuffer.clear()
        if (n == null) return
        var idx = channels.indexOfFirst { it.number == n }
        if (idx < 0 && channels.none { it.number > 0 } && n in 1..channels.size) idx = n - 1
        if (idx >= 0) tune(idx, immediate = true)
        else Toast.makeText(this, "$n numaralı kanal bu listede yok", Toast.LENGTH_SHORT).show()
    }

    // ---------------------------------------------------------------- movie / series

    private fun seriesProgress(ep: Episode, positionMs: Long, durationMs: Long): Progress =
        Progress(
            key = "SERIES:${PlayerSession.seriesId}",
            kind = Kind.SERIES,
            id = PlayerSession.seriesId,
            title = PlayerSession.seriesName,
            subtitle = "S${ep.season} B${ep.number} · ${ep.title}",
            image = PlayerSession.seriesCover,
            ext = ep.ext,
            episodeId = ep.id,
            season = ep.season,
            episode = ep.number,
            positionMs = positionMs,
            durationMs = durationMs,
            updatedAt = System.currentTimeMillis()
        )

    private fun saveProgress(finished: Boolean = false) {
        if (!::player.isInitialized || mode == MODE_LIVE) return
        val pos = player.currentPosition.coerceAtLeast(0L)
        val rawDuration = player.duration
        val dur = if (rawDuration == C.TIME_UNSET || rawDuration <= 0) 0L else rawDuration
        val nearEnd = finished || (dur > 0 && pos >= dur * 0.94)
        if (mode == MODE_MOVIE) {
            val key = "MOVIE:$movieId"
            if (nearEnd) {
                store.removeProgress(key)
            } else if (pos > 15_000) {
                store.saveProgress(
                    Progress(key, Kind.MOVIE, movieId, movieName, null, movieIcon, movieExt, null, 0, 0,
                        pos, dur, System.currentTimeMillis())
                )
            }
        } else {
            val idx = player.currentMediaItemIndex
            val ep = episodes.getOrNull(idx) ?: return
            if (nearEnd) {
                store.setWatched(ep.id)
                val next = episodes.getOrNull(idx + 1)
                if (next != null) store.saveProgress(seriesProgress(next, 0L, next.durationSec * 1000))
                else store.removeProgress("SERIES:${PlayerSession.seriesId}")
            } else if (pos > 15_000) {
                store.setEpisodePosition(ep.id, pos)
                store.saveProgress(seriesProgress(ep, pos, dur))
            }
        }
    }

    private fun updateTitle() {
        if (mode == MODE_MOVIE) {
            titleText.text = movieName
            subtitleText.visibility = View.GONE
        } else if (mode == MODE_SERIES) {
            val ep = episodes.getOrNull(player.currentMediaItemIndex) ?: return
            titleText.text = PlayerSession.seriesName
            subtitleText.text = "Sezon ${ep.season} · Bölüm ${ep.number} — ${ep.title}"
            subtitleText.visibility = View.VISIBLE
        }
    }

    private fun seekBy(deltaMs: Long) {
        val dur = player.duration
        val base = if (seekTarget >= 0) seekTarget else player.currentPosition
        var target = (base + deltaMs).coerceAtLeast(0L)
        if (dur != C.TIME_UNSET && dur > 0) target = target.coerceAtMost((dur - 1_000).coerceAtLeast(0L))
        seekTarget = target
        player.seekTo(target)
        val durText = if (dur != C.TIME_UNSET && dur > 0) " / ${formatTime(dur)}" else ""
        seekOverlay.text = "${if (deltaMs < 0) "◀◀" else "▶▶"}  ${formatTime(target)}$durText"
        seekOverlay.visibility = View.VISIBLE
        handler.removeCallbacks(hideSeekRunnable)
        handler.postDelayed(hideSeekRunnable, 1_200)
    }

    private fun seekStep(event: KeyEvent): Long = when {
        event.repeatCount > 20 -> 60_000L
        event.repeatCount > 6 -> 30_000L
        else -> 10_000L
    }

    private fun togglePlay() {
        if (player.playbackState == Player.STATE_ENDED) player.seekTo(0)
        if (player.playbackState == Player.STATE_IDLE) player.prepare()
        player.playWhenReady = !player.playWhenReady
    }

    // ---------------------------------------------------------------- options

    private data class TrackRef(val group: Tracks.Group, val index: Int, val label: String)

    private fun trackLabel(format: Format, index: Int, type: Int): String {
        val parts = mutableListOf<String>()
        format.label?.takeIf { it.isNotBlank() }?.let { parts += it }
        format.language?.takeIf { it.isNotBlank() && it != "und" }?.let { code ->
            val name = Locale(code).getDisplayLanguage(Locale("tr")).ifBlank { code }
            if (parts.none { it.equals(name, ignoreCase = true) }) parts += name
        }
        if (type == C.TRACK_TYPE_AUDIO) {
            when (format.channelCount) {
                1 -> parts += "Mono"
                2 -> parts += "Stereo"
                6 -> parts += "5.1"
                8 -> parts += "7.1"
            }
        }
        return parts.joinToString(" · ").ifBlank { "Parça ${index + 1}" }
    }

    private fun tracks(type: Int): List<TrackRef> =
        player.currentTracks.groups.filter { it.type == type }.flatMap { g ->
            (0 until g.length).filter { g.isTrackSupported(it) }.map { i -> TrackRef(g, i, trackLabel(g.getTrackFormat(i), i, type)) }
        }

    private fun pickTrack(type: Int) {
        val list = tracks(type)
        val isText = type == C.TRACK_TYPE_TEXT
        val names = (if (isText) listOf("Kapalı") else emptyList()) + list.map { it.label }
        val selected = list.indexOfFirst { it.group.isTrackSelected(it.index) }
        val checked = if (isText) {
            if (selected < 0) 0 else selected + 1
        } else {
            selected.coerceAtLeast(0)
        }
        AlertDialog.Builder(this)
            .setTitle(if (isText) "Altyazı" else "Ses")
            .setSingleChoiceItems(names.toTypedArray(), checked) { dialog, which ->
                val params = player.trackSelectionParameters.buildUpon()
                if (isText && which == 0) {
                    params.setTrackTypeDisabled(C.TRACK_TYPE_TEXT, true)
                } else {
                    val t = list[if (isText) which - 1 else which]
                    params.setTrackTypeDisabled(type, false)
                        .setOverrideForType(TrackSelectionOverride(t.group.mediaTrackGroup, t.index))
                }
                player.trackSelectionParameters = params.build()
                dialog.dismiss()
            }
            .show()
    }

    private fun cycleResize() {
        resizeIndex = (resizeIndex + 1) % resizeModes.size
        playerView.resizeMode = resizeModes[resizeIndex]
        Toast.makeText(this, "Görüntü: ${resizeNames[resizeIndex]}", Toast.LENGTH_SHORT).show()
    }

    private fun showOptions() {
        val labels = mutableListOf<String>()
        val actions = mutableListOf<() -> Unit>()
        if (mode == MODE_LIVE) {
            channels.getOrNull(current)?.let { ch ->
                labels += if (store.isFavorite(ch.key)) "Favorilerden çıkar" else "Favorilere ekle"
                actions += { toggleFavorite(ch) }
            }
        }
        labels += "Görüntü oranı: ${resizeNames[resizeIndex]}"
        actions += { cycleResize() }
        if (tracks(C.TRACK_TYPE_AUDIO).size > 1) {
            labels += "Ses"
            actions += { pickTrack(C.TRACK_TYPE_AUDIO) }
        }
        if (tracks(C.TRACK_TYPE_TEXT).isNotEmpty()) {
            labels += "Altyazı"
            actions += { pickTrack(C.TRACK_TYPE_TEXT) }
        }
        if (mode == MODE_LIVE) {
            labels += "Yayını yeniden başlat"
            actions += { startLive() }
        }
        AlertDialog.Builder(this)
            .setTitle("Seçenekler")
            .setItems(labels.toTypedArray()) { _, which -> actions.getOrNull(which)?.invoke() }
            .show()
    }

    // ---------------------------------------------------------------- keys

    private fun isCenter(code: Int) =
        code == KeyEvent.KEYCODE_DPAD_CENTER || code == KeyEvent.KEYCODE_ENTER || code == KeyEvent.KEYCODE_NUMPAD_ENTER

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (!::player.isInitialized) return super.dispatchKeyEvent(event)
        val code = event.keyCode
        if (event.action == KeyEvent.ACTION_UP && consumedKeys.remove(code)) {
            if (isCenter(code) && !centerLongHandled) onCenterShortPress()
            return true
        }
        if (event.action == KeyEvent.ACTION_DOWN) {
            val handled = if (mode == MODE_LIVE) liveKeyDown(event) else vodKeyDown(event)
            if (handled) {
                consumedKeys.add(code)
                return true
            }
        }
        return super.dispatchKeyEvent(event)
    }

    private fun handleCenterDown(event: KeyEvent) {
        if (event.repeatCount == 0) {
            centerLongHandled = false
        } else if (!centerLongHandled && (event.isLongPress || event.repeatCount >= 3)) {
            centerLongHandled = true
            showOptions()
        }
    }

    private fun onCenterShortPress() {
        if (mode == MODE_LIVE) {
            openPanel()
        } else {
            togglePlay()
            playerView.showController()
        }
    }

    private fun liveKeyDown(event: KeyEvent): Boolean {
        val code = event.keyCode
        val first = event.repeatCount == 0
        if (channelPanel.visibility == View.VISIBLE) {
            if (code == KeyEvent.KEYCODE_DPAD_RIGHT) {
                if (first) closePanel()
                return true
            }
            return false
        }
        when (code) {
            KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_NUMPAD_ENTER -> {
                handleCenterDown(event)
                return true
            }
            KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_CHANNEL_DOWN, KeyEvent.KEYCODE_PAGE_DOWN -> {
                zap(-1, !first)
                return true
            }
            KeyEvent.KEYCODE_DPAD_DOWN, KeyEvent.KEYCODE_CHANNEL_UP, KeyEvent.KEYCODE_PAGE_UP -> {
                zap(+1, !first)
                return true
            }
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_LAST_CHANNEL -> {
                if (first && previous in channels.indices) tune(previous, immediate = true)
                return true
            }
            KeyEvent.KEYCODE_DPAD_RIGHT, KeyEvent.KEYCODE_INFO -> {
                if (first) {
                    if (infoBar.visibility == View.VISIBLE) hideInfo() else showInfo(8_000)
                }
                return true
            }
            KeyEvent.KEYCODE_GUIDE -> {
                if (first) openPanel()
                return true
            }
            KeyEvent.KEYCODE_MENU -> {
                if (first) showOptions()
                return true
            }
            KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, KeyEvent.KEYCODE_MEDIA_PLAY, KeyEvent.KEYCODE_MEDIA_PAUSE -> {
                if (first) {
                    if (player.playbackState == Player.STATE_IDLE) startLive() else player.playWhenReady = !player.playWhenReady
                }
                return true
            }
        }
        if (code in KeyEvent.KEYCODE_0..KeyEvent.KEYCODE_9) {
            if (first) appendDigit(code - KeyEvent.KEYCODE_0)
            return true
        }
        if (code in KeyEvent.KEYCODE_NUMPAD_0..KeyEvent.KEYCODE_NUMPAD_9) {
            if (first) appendDigit(code - KeyEvent.KEYCODE_NUMPAD_0)
            return true
        }
        return false
    }

    private fun vodKeyDown(event: KeyEvent): Boolean {
        val code = event.keyCode
        val first = event.repeatCount == 0
        when (code) {
            KeyEvent.KEYCODE_MEDIA_FAST_FORWARD -> {
                seekBy(30_000)
                return true
            }
            KeyEvent.KEYCODE_MEDIA_REWIND -> {
                seekBy(-30_000)
                return true
            }
            KeyEvent.KEYCODE_MENU -> {
                if (first) showOptions()
                return true
            }
            KeyEvent.KEYCODE_MEDIA_NEXT, KeyEvent.KEYCODE_CHANNEL_UP -> {
                if (first && mode == MODE_SERIES && player.hasNextMediaItem()) {
                    saveProgress()
                    player.seekToNextMediaItem()
                }
                return true
            }
            KeyEvent.KEYCODE_MEDIA_PREVIOUS, KeyEvent.KEYCODE_CHANNEL_DOWN -> {
                if (first && mode == MODE_SERIES && player.hasPreviousMediaItem()) {
                    saveProgress()
                    player.seekToPreviousMediaItem()
                }
                return true
            }
        }
        // When the controller is open its buttons and seek bar own the D-pad.
        if (playerView.isControllerFullyVisible) return false
        when (code) {
            KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_NUMPAD_ENTER -> {
                handleCenterDown(event)
                return true
            }
            KeyEvent.KEYCODE_DPAD_LEFT -> {
                seekBy(-seekStep(event))
                return true
            }
            KeyEvent.KEYCODE_DPAD_RIGHT -> {
                seekBy(seekStep(event))
                return true
            }
            KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_DOWN -> {
                if (first) playerView.showController()
                return true
            }
            KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, KeyEvent.KEYCODE_MEDIA_PLAY, KeyEvent.KEYCODE_MEDIA_PAUSE -> {
                if (first) {
                    togglePlay()
                    playerView.showController()
                }
                return true
            }
        }
        return false
    }

    private fun onBack() {
        if (mode == MODE_LIVE) {
            when {
                channelPanel.visibility == View.VISIBLE -> closePanel()
                infoBar.visibility == View.VISIBLE -> hideInfo()
                else -> finish()
            }
        } else {
            if (playerView.isControllerFullyVisible) {
                playerView.hideController()
            } else {
                saveProgress()
                finish()
            }
        }
    }

    // ---------------------------------------------------------------- lifecycle

    override fun onStart() {
        super.onStart()
        if (stoppedInBackground && ::player.isInitialized) {
            stoppedInBackground = false
            if (mode == MODE_LIVE) {
                startLive()
            } else {
                player.prepare()
                player.playWhenReady = true
                handler.removeCallbacks(saveRunnable)
                handler.postDelayed(saveRunnable, 10_000)
            }
        }
    }

    override fun onStop() {
        super.onStop()
        if (!::player.isInitialized) return
        saveProgress()
        handler.removeCallbacks(saveRunnable)
        if (mode == MODE_LIVE) player.stop() else player.pause()
        stoppedInBackground = true
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        epgJob?.cancel()
        if (::player.isInitialized) {
            player.removeListener(playerListener)
            player.release()
        }
        super.onDestroy()
    }
}
