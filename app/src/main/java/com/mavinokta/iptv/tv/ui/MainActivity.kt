package com.mavinokta.iptv.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.Editable
import android.text.TextWatcher
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.mavinokta.iptv.tv.R
import com.mavinokta.iptv.tv.data.Category
import com.mavinokta.iptv.tv.data.Kind
import com.mavinokta.iptv.tv.data.MediaEntry
import com.mavinokta.iptv.tv.data.PlayerSession
import com.mavinokta.iptv.tv.data.Repo
import com.mavinokta.iptv.tv.data.normalizeForSearch
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : AppCompatActivity() {

    enum class Section(val title: String) {
        CONTINUE("İzlemeye Devam"),
        LIVE("Canlı TV"),
        MOVIES("Filmler"),
        SERIES("Diziler"),
        FAVORITES("Favoriler"),
        SEARCH("Ara"),
        ACCOUNT("Hesap")
    }

    private lateinit var menuList: RecyclerView
    private lateinit var categoryPane: View
    private lateinit var categoryTitle: TextView
    private lateinit var categoryList: RecyclerView
    private lateinit var searchInput: EditText
    private lateinit var contentTitle: TextView
    private lateinit var contentGrid: RecyclerView
    private lateinit var loading: ProgressBar
    private lateinit var emptyText: TextView

    private lateinit var menuAdapter: TextListAdapter
    private lateinit var categoryAdapter: TextListAdapter
    private lateinit var contentAdapter: ContentAdapter
    private lateinit var gridLayout: GridLayoutManager

    private val handler = Handler(Looper.getMainLooper())
    private var section: Section? = null
    private var pendingSection: Section? = null
    private var categories: List<Category> = emptyList()
    private var selectedCategory = -1
    private var pendingCategory = -1
    private var focusCategoriesWhenLoaded = false
    private var lastQuery = ""
    // Category to reopen per kind: used once, the first time that section loads after app start.
    private val restoreCategory = HashMap<Kind, String?>()

    private var categoryJob: Job? = null
    private var contentJob: Job? = null

    private val store get() = Repo.store

    private val sectionRunnable = Runnable {
        val s = pendingSection ?: return@Runnable
        if (s != section) selectSection(s, fromClick = false)
    }
    private val categoryRunnable = Runnable {
        if (pendingCategory != selectedCategory) selectCategory(pendingCategory)
    }
    private val searchRunnable = Runnable { runSearch(searchInput.text.toString()) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Repo.init(this)
        if (store.account == null) {
            startActivity(Intent(this, LoginActivity::class.java))
            finish()
            return
        }
        setContentView(R.layout.activity_main)

        menuList = findViewById(R.id.menuList)
        categoryPane = findViewById(R.id.categoryPane)
        categoryTitle = findViewById(R.id.categoryTitle)
        categoryList = findViewById(R.id.categoryList)
        searchInput = findViewById(R.id.searchInput)
        contentTitle = findViewById(R.id.contentTitle)
        contentGrid = findViewById(R.id.contentGrid)
        loading = findViewById(R.id.loading)
        emptyText = findViewById(R.id.emptyText)

        menuAdapter = TextListAdapter(
            R.layout.item_text,
            onFocus = { i -> scheduleSection(Section.entries[i]) },
            onClick = { i -> selectSection(Section.entries[i], fromClick = true) }
        )
        menuList.layoutManager = LinearLayoutManager(this)
        menuList.itemAnimator = null
        menuList.adapter = menuAdapter

        categoryAdapter = TextListAdapter(
            R.layout.item_text,
            onFocus = { i -> scheduleCategory(i) },
            onClick = { i ->
                handler.removeCallbacks(categoryRunnable)
                if (i != selectedCategory) selectCategory(i)
                if (contentAdapter.itemCount > 0) contentGrid.focusFirstVisible()
            }
        )
        categoryList.layoutManager = LinearLayoutManager(this)
        categoryList.itemAnimator = null
        categoryList.adapter = categoryAdapter

        contentAdapter = ContentAdapter(::onTileClick, ::onTileLongClick)
        gridLayout = GridLayoutManager(this, 8)
        gridLayout.spanSizeLookup = object : GridLayoutManager.SpanSizeLookup() {
            override fun getSpanSize(position: Int): Int =
                if (contentAdapter.isLive(position)) liveSpan() else 2
        }
        contentGrid.layoutManager = gridLayout
        contentGrid.itemAnimator = null
        contentGrid.setHasFixedSize(true)
        contentGrid.setItemViewCacheSize(24)
        contentGrid.adapter = contentAdapter
        contentGrid.addOnLayoutChangeListener { _, left, _, right, _, oldLeft, _, oldRight, _ ->
            if (right - left != oldRight - oldLeft) updateSpans(right - left)
        }

        searchInput.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                if (section != Section.SEARCH) return
                handler.removeCallbacks(searchRunnable)
                handler.postDelayed(searchRunnable, 600)
            }
        })
        searchInput.setOnEditorActionListener { _, actionId, event ->
            val enter = event?.keyCode == KeyEvent.KEYCODE_ENTER && event.action == KeyEvent.ACTION_DOWN
            if (actionId == EditorInfo.IME_ACTION_SEARCH || actionId == EditorInfo.IME_ACTION_DONE || enter) {
                hideKeyboard()
                handler.removeCallbacks(searchRunnable)
                runSearch(searchInput.text.toString(), focusResults = true)
                true
            } else {
                false
            }
        }

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = handleBack()
        })

        menuAdapter.submit(Section.entries.map { it.title }, -1)
        for (k in Kind.entries) restoreCategory[k] = store.lastCategory(k)
        val saved = store.lastSection?.let { n -> Section.entries.firstOrNull { it.name == n } }
        val first = when {
            saved != null && saved != Section.SEARCH && saved != Section.ACCOUNT &&
                !(saved == Section.CONTINUE && store.progressList().isEmpty()) -> saved
            store.progressList().isNotEmpty() -> Section.CONTINUE
            else -> Section.LIVE
        }
        selectSection(first, fromClick = false)
        menuList.post { menuList.focusRow(first.ordinal) }
    }

    override fun onResume() {
        super.onResume()
        if (!::contentAdapter.isInitialized) return
        when (section) {
            Section.CONTINUE -> showContinue(keepFocus = true)
            Section.FAVORITES -> showFavorites(keepFocus = true)
            Section.LIVE -> {
                if (currentCategoryId() == RECENT_ID) selectCategory(selectedCategory, force = true)
                else refreshFavoriteMarks()
                ensureRecentCategory()
            }
            else -> refreshFavoriteMarks()
        }
    }

    private fun currentCategoryId(): String? = categories.getOrNull(selectedCategory)?.id

    /** "Son İzlenenler" appears in the live list as soon as a channel has been watched. */
    private fun ensureRecentCategory() {
        if (section != Section.LIVE || categories.isEmpty()) return
        if (categories.any { it.id == RECENT_ID } || store.recentChannels().isEmpty()) return
        categories = categories + Category(RECENT_ID, RECENT_TITLE)
        categoryAdapter.submit(categories.map { it.name }, selectedCategory)
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    // ---------------------------------------------------------------- sections

    private fun kindOf(s: Section): Kind? = when (s) {
        Section.LIVE -> Kind.LIVE
        Section.MOVIES -> Kind.MOVIE
        Section.SERIES -> Kind.SERIES
        else -> null
    }

    private fun scheduleSection(s: Section) {
        handler.removeCallbacks(sectionRunnable)
        if (s == Section.ACCOUNT) return
        pendingSection = s
        handler.postDelayed(sectionRunnable, 250)
    }

    private fun selectSection(s: Section, fromClick: Boolean) {
        handler.removeCallbacks(sectionRunnable)
        if (s == Section.ACCOUNT) {
            if (fromClick) showAccountDialog()
            return
        }
        val kind = kindOf(s)
        val reload = s != section || (fromClick && kind != null && categories.isEmpty())
        section = s
        if (s != Section.SEARCH) store.lastSection = s.name
        menuAdapter.setActivated(s.ordinal)

        if (reload) {
            categoryJob?.cancel()
            contentJob?.cancel()
            handler.removeCallbacks(categoryRunnable)
            handler.removeCallbacks(searchRunnable)
            categories = emptyList()
            selectedCategory = -1
            categoryAdapter.submit(emptyList(), -1)
            focusCategoriesWhenLoaded = false

            if (kind != null) {
                categoryPane.visibility = View.VISIBLE
                searchInput.visibility = View.GONE
                categoryTitle.text = s.title
                showTiles(emptyList(), null, keepFocus = false)
                contentTitle.text = ""
                loadCategories(kind)
            } else {
                categoryPane.visibility = View.GONE
                searchInput.visibility = if (s == Section.SEARCH) View.VISIBLE else View.GONE
                when (s) {
                    Section.CONTINUE -> showContinue(keepFocus = false)
                    Section.FAVORITES -> showFavorites(keepFocus = false)
                    Section.SEARCH -> {
                        contentTitle.text = "Arama"
                        if (lastQuery.length >= 2) runSearch(lastQuery) else showTiles(emptyList(), "En az 2 harf yaz", false)
                    }
                    else -> Unit
                }
            }
        }

        if (fromClick) {
            when {
                kind != null -> {
                    if (categories.isNotEmpty()) categoryList.focusRow(selectedCategory.coerceAtLeast(0))
                    else focusCategoriesWhenLoaded = true
                }
                s == Section.SEARCH -> {
                    searchInput.requestFocus()
                    showKeyboard()
                }
                else -> if (contentAdapter.itemCount > 0) contentGrid.focusFirstVisible()
            }
        }
    }

    private fun loadCategories(kind: Kind) {
        categoryJob = lifecycleScope.launch {
            setLoading(true)
            try {
                val cats = Repo.categories(kind)
                var all = cats + Category(ALL_ID, "Tümü")
                if (kind == Kind.LIVE && store.recentChannels().isNotEmpty()) all = all + Category(RECENT_ID, RECENT_TITLE)
                categories = all
                val wanted = restoreCategory.remove(kind)
                val start = if (wanted != null) all.indexOfFirst { it.id == wanted }.coerceAtLeast(0) else 0
                selectedCategory = start
                categoryAdapter.submit(all.map { it.name }, start)
                selectCategory(start, force = true)
                if (focusCategoriesWhenLoaded) {
                    focusCategoriesWhenLoaded = false
                    categoryList.focusRow(start)
                }
                prefetchCategories(kind)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                setLoading(false)
                showTiles(emptyList(), "Liste alınamadı: ${e.message ?: "bağlantı hatası"}\nMenüden bölümü tekrar seçerek yeniden dene.", false)
            }
        }
    }

    // Category lists are tiny; fetching the other two sections' lists while the user looks at the
    // first makes switching sections instant.
    private fun prefetchCategories(current: Kind) {
        lifecycleScope.launch {
            delay(1_500)
            for (k in Kind.entries) {
                if (k == current) continue
                try {
                    Repo.categories(k)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    // not needed yet
                }
            }
        }
    }

    private fun scheduleCategory(index: Int) {
        handler.removeCallbacks(categoryRunnable)
        pendingCategory = index
        handler.postDelayed(categoryRunnable, 300)
    }

    private fun selectCategory(index: Int, force: Boolean = false) {
        val kind = section?.let { kindOf(it) } ?: return
        val cat = categories.getOrNull(index) ?: return
        if (!force && index == selectedCategory && contentAdapter.itemCount > 0) return
        selectedCategory = index
        categoryAdapter.setActivated(index)
        store.setLastCategory(kind, cat.id)
        contentJob?.cancel()
        if (cat.id == RECENT_ID) {
            val list = store.recentChannels()
            showStreams(cat, list, keepFocus = !force && contentAdapter.itemCount > 0)
            return
        }
        contentJob = lifecycleScope.launch {
            contentTitle.text = cat.name
            setLoading(true)
            try {
                val list = Repo.streams(kind, if (cat.id == ALL_ID) null else cat.id) { fresh ->
                    // A background refresh found changes: update in place if the user is still here.
                    if (!isDestroyed && selectedCategory == index && section?.let { kindOf(it) } == kind) {
                        showStreams(cat, fresh, keepFocus = true)
                    }
                }
                showStreams(cat, list, keepFocus = false)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                showTiles(emptyList(), "İçerik alınamadı: ${e.message ?: "bağlantı hatası"}\nKategoriyi tekrar seçerek yeniden dene.", false)
            }
        }
    }

    private fun showStreams(cat: Category, list: List<MediaEntry>, keepFocus: Boolean) {
        val favs = store.favoriteKeys()
        contentTitle.text = if (list.isEmpty()) cat.name else "${cat.name} · ${list.size}"
        showTiles(list.map { Tile.Media(it, it.key in favs, false) }, "Bu kategoride içerik yok", keepFocus)
    }

    private fun showContinue(keepFocus: Boolean) {
        contentTitle.text = Section.CONTINUE.title
        val tiles = store.progressList().map { Tile.Resume(it) }
        showTiles(tiles, "Yarım kalan film veya dizi yok.", keepFocus)
    }

    private fun showFavorites(keepFocus: Boolean) {
        contentTitle.text = Section.FAVORITES.title
        val tiles = store.favorites().map { Tile.Media(it, favorite = true, showKind = it.kind != Kind.LIVE) }
        showTiles(tiles, "Favori yok. Bir kanal, film veya dizinin üzerinde OK tuşuna basılı tutarak ekleyebilirsin.", keepFocus)
    }

    private fun refreshFavoriteMarks() {
        val favs = store.favoriteKeys()
        contentAdapter.items.forEachIndexed { i, t ->
            if (t is Tile.Media && t.favorite != (t.entry.key in favs)) {
                contentAdapter.replace(i, t.copy(favorite = !t.favorite))
            }
        }
    }

    // ---------------------------------------------------------------- search

    private fun runSearch(query: String, focusResults: Boolean = false) {
        val q = query.trim()
        lastQuery = q
        contentJob?.cancel()
        if (q.length < 2) {
            contentTitle.text = "Arama"
            showTiles(emptyList(), "En az 2 harf yaz", keepFocus = false)
            return
        }
        contentJob = lifecycleScope.launch {
            contentTitle.text = "Aranıyor: $q"
            setLoading(true)
            val needle = normalizeForSearch(q)
            val results = ArrayList<MediaEntry>()
            var failures = 0
            var shown = false
            val kinds = listOf(Kind.LIVE, Kind.MOVIE, Kind.SERIES)
            for ((n, kind) in kinds.withIndex()) {
                try {
                    val all = Repo.streams(kind, null)
                    val names = Repo.searchIndex(kind, all)
                    val found = withContext(Dispatchers.Default) {
                        val hits = ArrayList<MediaEntry>()
                        for (i in names.indices) {
                            if (hits.size >= 300) break
                            if (names[i].contains(needle)) hits.add(all[i])
                        }
                        hits
                    }
                    results.addAll(found)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    failures++
                }
                // Show what is found so far instead of waiting for the slowest catalogue.
                if (n < kinds.size - 1 && results.isNotEmpty()) {
                    val favs = store.favoriteKeys()
                    contentTitle.text = "\"$q\" · ${results.size} sonuç · aranıyor…"
                    showTiles(results.map { Tile.Media(it, it.key in favs, showKind = it.kind != Kind.LIVE) }, null, keepFocus = shown)
                    if (focusResults && !shown) contentGrid.focusFirstVisible()
                    shown = true
                }
            }
            val favs = store.favoriteKeys()
            contentTitle.text = "\"$q\" · ${results.size} sonuç"
            val empty = if (failures == 3) "Arama yapılamadı, bağlantıyı kontrol et." else "Sonuç bulunamadı"
            showTiles(results.map { Tile.Media(it, it.key in favs, showKind = it.kind != Kind.LIVE) }, empty, keepFocus = shown)
            if (focusResults && !shown && results.isNotEmpty()) contentGrid.focusFirstVisible()
        }
    }

    // ---------------------------------------------------------------- grid

    private fun liveSpan(): Int {
        val span = gridLayout.spanCount
        return if (span >= 12 && span % 3 == 0) span / 3 else span / 2
    }

    private fun updateSpans(widthPx: Int) {
        if (widthPx <= 0) return
        val density = resources.displayMetrics.density
        val columns = (widthPx / (118 * density)).toInt().coerceIn(3, 8)
        val span = columns * 2
        if (gridLayout.spanCount != span) {
            gridLayout.spanCount = span
            contentGrid.post { contentAdapter.notifyItemRangeChanged(0, contentAdapter.itemCount) }
        }
    }

    // A slow panel should not look like a frozen app.
    private val slowRunnable = Runnable {
        if (loading.visibility != View.VISIBLE) return@Runnable
        emptyText.text = "Sunucu yavaş yanıt veriyor, bekleniyor…"
        emptyText.translationY = 64 * resources.displayMetrics.density
        emptyText.visibility = View.VISIBLE
    }

    private fun setLoading(on: Boolean) {
        loading.visibility = if (on) View.VISIBLE else View.GONE
        handler.removeCallbacks(slowRunnable)
        emptyText.translationY = 0f
        if (on) {
            emptyText.visibility = View.GONE
            handler.postDelayed(slowRunnable, 7000)
        }
    }

    private fun showTiles(tiles: List<Tile>, emptyMessage: String?, keepFocus: Boolean) {
        setLoading(false)
        val hadFocus = contentGrid.hasFocus()
        val focusedPos = currentFocus?.let { f ->
            if (f.isInside(contentGrid)) contentGrid.findContainingViewHolder(f)?.bindingAdapterPosition else null
        } ?: -1
        contentAdapter.submit(tiles)
        if (!keepFocus) contentGrid.scrollToPosition(0)
        if (tiles.isEmpty() && emptyMessage != null) {
            emptyText.text = emptyMessage
            emptyText.visibility = View.VISIBLE
        } else {
            emptyText.visibility = View.GONE
        }
        if (hadFocus) {
            if (tiles.isEmpty()) {
                focusBackToSide()
            } else {
                contentGrid.focusRow(if (keepFocus && focusedPos >= 0) focusedPos else 0)
            }
        }
    }

    private fun focusBackToSide() {
        if (categoryPane.visibility == View.VISIBLE && categoryAdapter.itemCount > 0) {
            categoryList.focusRow(selectedCategory.coerceAtLeast(0))
        } else if (searchInput.visibility == View.VISIBLE) {
            searchInput.requestFocus()
        } else {
            menuList.focusRow(section?.ordinal ?: 0)
        }
    }

    private fun onTileClick(tile: Tile) {
        when (tile) {
            is Tile.Media -> when (tile.entry.kind) {
                Kind.LIVE -> {
                    val lives = contentAdapter.items.mapNotNull { (it as? Tile.Media)?.entry }.filter { it.kind == Kind.LIVE }
                    val index = lives.indexOfFirst { it.key == tile.entry.key }.coerceAtLeast(0)
                    PlayerSession.liveChannels = lives
                    PlayerSession.liveTitle = contentTitle.text.toString()
                    startActivity(PlayerActivity.live(this, index))
                }
                Kind.MOVIE -> openMovie(tile.entry)
                Kind.SERIES -> startActivity(SeriesActivity.intent(this, tile.entry.id, tile.entry.name, tile.entry.icon))
            }
            is Tile.Resume -> {
                val p = tile.progress
                if (p.kind == Kind.MOVIE) {
                    val entry = MediaEntry(Kind.MOVIE, p.id, p.title, p.image, p.ext ?: "mp4", null)
                    startActivity(PlayerActivity.movie(this, entry, p.positionMs))
                } else {
                    startActivity(SeriesActivity.intent(this, p.id, p.title, p.image))
                }
            }
        }
    }

    private fun openMovie(entry: MediaEntry) {
        val p = store.progress(entry.key)
        if (p != null && p.positionMs > 30_000) {
            val options = arrayOf("Kaldığın yerden devam et (${formatTime(p.positionMs)})", "Baştan başla")
            AlertDialog.Builder(this)
                .setTitle(entry.name)
                .setItems(options) { _, which ->
                    startActivity(PlayerActivity.movie(this, entry, if (which == 0) p.positionMs else 0L))
                }
                .show()
        } else {
            startActivity(PlayerActivity.movie(this, entry, 0L))
        }
    }

    private fun onTileLongClick(tile: Tile, position: Int) {
        when (tile) {
            is Tile.Media -> {
                val added = store.toggleFavorite(tile.entry)
                Toast.makeText(this, if (added) "Favorilere eklendi" else "Favorilerden çıkarıldı", Toast.LENGTH_SHORT).show()
                if (section == Section.FAVORITES) showFavorites(keepFocus = true)
                else contentAdapter.replace(position, tile.copy(favorite = added))
            }
            is Tile.Resume -> {
                AlertDialog.Builder(this)
                    .setTitle(tile.progress.title)
                    .setItems(arrayOf("İzlemeye Devam'dan kaldır", "Vazgeç")) { _, which ->
                        if (which == 0) {
                            store.removeProgress(tile.progress.key)
                            showContinue(keepFocus = true)
                        }
                    }
                    .show()
            }
        }
    }

    // ---------------------------------------------------------------- account

    private fun showAccountDialog() {
        val acc = store.account ?: return
        val dialog = AlertDialog.Builder(this)
            .setTitle("Hesap")
            .setMessage("${acc.username}\n${acc.server}")
            .setPositiveButton("Hesabı değiştir") { _, _ ->
                startActivity(Intent(this, LoginActivity::class.java))
            }
            .setNeutralButton("Listeyi yenile") { _, _ ->
                Repo.refreshAll()
                val current = section ?: Section.LIVE
                section = null
                selectSection(current, fromClick = false)
                Toast.makeText(this, "Liste yenileniyor", Toast.LENGTH_SHORT).show()
            }
            .setNegativeButton("Kapat", null)
            .create()
        dialog.setOnShowListener { dialog.getButton(AlertDialog.BUTTON_NEGATIVE)?.requestFocus() }
        dialog.show()
    }

    // ---------------------------------------------------------------- keys

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.action == KeyEvent.ACTION_DOWN && ::contentGrid.isInitialized) {
            val f = currentFocus
            if (f != null && handleNavigation(event.keyCode, f)) return true
        }
        return super.dispatchKeyEvent(event)
    }

    /** Keeps D-pad focus inside the three columns and moves between them predictably. */
    private fun handleNavigation(keyCode: Int, f: View): Boolean {
        val inMenu = f.isInside(menuList)
        val inCategories = f.isInside(categoryList)
        val inGrid = f.isInside(contentGrid)
        val inSearch = f === searchInput
        val categoriesShown = categoryPane.visibility == View.VISIBLE && categoryAdapter.itemCount > 0
        val sectionIndex = section?.ordinal ?: 0

        when (keyCode) {
            KeyEvent.KEYCODE_DPAD_LEFT -> {
                if (inGrid && nextFocusInside(contentGrid, f, View.FOCUS_LEFT) == null) {
                    if (categoriesShown) categoryList.focusRow(selectedCategory.coerceAtLeast(0))
                    else menuList.focusRow(sectionIndex)
                    return true
                }
                if (inCategories) {
                    menuList.focusRow(sectionIndex)
                    return true
                }
                if (inMenu) return true
            }
            KeyEvent.KEYCODE_DPAD_RIGHT -> {
                if (inMenu) {
                    when {
                        section == Section.ACCOUNT -> Unit
                        categoriesShown -> categoryList.focusRow(selectedCategory.coerceAtLeast(0))
                        searchInput.visibility == View.VISIBLE -> searchInput.requestFocus()
                        contentAdapter.itemCount > 0 -> contentGrid.focusFirstVisible()
                    }
                    return true
                }
                if (inCategories) {
                    handler.removeCallbacks(categoryRunnable)
                    if (pendingCategory >= 0 && pendingCategory != selectedCategory) selectCategory(pendingCategory)
                    else if (contentAdapter.itemCount > 0) contentGrid.focusFirstVisible()
                    return true
                }
                if (inGrid && nextFocusInside(contentGrid, f, View.FOCUS_RIGHT) == null) return true
            }
            KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_DOWN -> {
                val up = keyCode == KeyEvent.KEYCODE_DPAD_UP
                if (inSearch) {
                    if (!up && contentAdapter.itemCount > 0) contentGrid.focusFirstVisible()
                    return true
                }
                val list = when {
                    inMenu -> menuList
                    inCategories -> categoryList
                    inGrid -> contentGrid
                    else -> null
                } ?: return false
                if (nextFocusInside(list, f, if (up) View.FOCUS_UP else View.FOCUS_DOWN) == null) {
                    if (inGrid && up && searchInput.visibility == View.VISIBLE) {
                        searchInput.requestFocus()
                        return true
                    }
                    // Let RecyclerView scroll to rows that are not laid out yet; otherwise stay put.
                    return !list.canScrollVertically(if (up) -1 else 1)
                }
            }
        }
        return false
    }

    private fun handleBack() {
        val f = currentFocus
        when {
            f != null && f.isInside(contentGrid) -> focusBackToSide()
            f != null && f === searchInput -> menuList.focusRow(section?.ordinal ?: 0)
            f != null && f.isInside(categoryList) -> menuList.focusRow(section?.ordinal ?: 0)
            else -> finish()
        }
    }

    private fun showKeyboard() {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as? InputMethodManager
        searchInput.post { imm?.showSoftInput(searchInput, InputMethodManager.SHOW_IMPLICIT) }
    }

    private fun hideKeyboard() {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as? InputMethodManager
        imm?.hideSoftInputFromWindow(searchInput.windowToken, 0)
    }

    companion object {
        private const val ALL_ID = "__all__"
        private const val RECENT_ID = "__recent__"
        private const val RECENT_TITLE = "Son İzlenenler"
    }
}
