package com.mavinokta.iptv.tv.ui

import android.graphics.Bitmap
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import coil.dispose
import coil.load
import com.mavinokta.iptv.tv.R
import com.mavinokta.iptv.tv.data.Episode
import com.mavinokta.iptv.tv.data.Kind
import com.mavinokta.iptv.tv.data.MediaEntry
import com.mavinokta.iptv.tv.data.Progress
import com.mavinokta.iptv.tv.data.Repo

internal fun ImageView.loadPoster(url: String?) {
    if (url.isNullOrBlank()) {
        dispose()
        setImageResource(R.drawable.ph_poster)
    } else {
        load(url) {
            crossfade(false)
            // Posters are opaque: 16-bit decoding halves their memory, which matters on 1 GB boxes.
            bitmapConfig(Bitmap.Config.RGB_565)
            placeholder(R.drawable.ph_poster)
            error(R.drawable.ph_poster)
        }
    }
}

internal fun ImageView.loadLogo(url: String?) {
    if (url.isNullOrBlank()) {
        dispose()
        setImageDrawable(null)
    } else {
        load(url) {
            crossfade(false)
            error(android.R.color.transparent)
        }
    }
}

internal fun View.focusScale(scale: Float = 1.05f) {
    setOnFocusChangeListener { v, has ->
        v.animate().scaleX(if (has) scale else 1f).scaleY(if (has) scale else 1f).setDuration(120).start()
        v.translationZ = if (has) 8f else 0f
    }
}

internal fun formatTime(ms: Long): String {
    val total = (ms / 1000).coerceAtLeast(0)
    val h = total / 3600
    val m = (total % 3600) / 60
    val s = total % 60
    return if (h > 0) String.format("%d:%02d:%02d", h, m, s) else String.format("%d:%02d", m, s)
}

/** Simple focusable text rows: main menu, categories, seasons. */
class TextListAdapter(
    private val layoutRes: Int,
    private val onFocus: (Int) -> Unit,
    private val onClick: (Int) -> Unit
) : RecyclerView.Adapter<TextListAdapter.VH>() {

    var items: List<String> = emptyList()
        private set
    var activated: Int = -1
        private set

    fun submit(list: List<String>, activatedIndex: Int) {
        items = list
        activated = activatedIndex
        notifyDataSetChanged()
    }

    fun setActivated(index: Int) {
        val old = activated
        activated = index
        if (old in items.indices) notifyItemChanged(old)
        if (index in items.indices && index != old) notifyItemChanged(index)
    }

    class VH(val text: TextView) : RecyclerView.ViewHolder(text)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val view = LayoutInflater.from(parent.context).inflate(layoutRes, parent, false) as TextView
        val holder = VH(view)
        view.setOnClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onClick(p)
        }
        view.setOnFocusChangeListener { _, has ->
            if (has) {
                val p = holder.bindingAdapterPosition
                if (p != RecyclerView.NO_POSITION) onFocus(p)
            }
        }
        return holder
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.text.text = items[position]
        holder.text.isActivated = position == activated
    }
}

sealed class Tile {
    data class Media(val entry: MediaEntry, val favorite: Boolean, val showKind: Boolean) : Tile()
    data class Resume(val progress: Progress) : Tile()
}

/** Catalogue grid: channel tiles (wide) and poster tiles (movies, series, continue watching). */
class ContentAdapter(
    private val onClick: (Tile) -> Unit,
    private val onLongClick: (Tile, Int) -> Unit
) : RecyclerView.Adapter<RecyclerView.ViewHolder>() {

    var items: List<Tile> = emptyList()
        private set

    fun submit(list: List<Tile>) {
        items = list
        notifyDataSetChanged()
    }

    fun replace(position: Int, tile: Tile) {
        if (position !in items.indices) return
        items = items.toMutableList().also { it[position] = tile }
        notifyItemChanged(position)
    }

    fun isLive(position: Int): Boolean {
        val t = items.getOrNull(position)
        return t is Tile.Media && t.entry.kind == Kind.LIVE
    }

    override fun getItemViewType(position: Int): Int = if (isLive(position)) TYPE_LIVE else TYPE_POSTER

    override fun getItemCount(): Int = items.size

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        val inflater = LayoutInflater.from(parent.context)
        val holder = if (viewType == TYPE_LIVE) {
            LiveVH(inflater.inflate(R.layout.item_live_tile, parent, false))
        } else {
            PosterVH(inflater.inflate(R.layout.item_poster_tile, parent, false))
        }
        holder.itemView.setOnClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onClick(items[p])
        }
        holder.itemView.setOnLongClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onLongClick(items[p], p)
            true
        }
        holder.itemView.focusScale(if (viewType == TYPE_LIVE) 1.03f else 1.06f)
        return holder
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val tile = items[position]
        when (holder) {
            is LiveVH -> {
                val t = tile as Tile.Media
                holder.logo.loadLogo(t.entry.icon)
                holder.number.text = if (t.entry.number > 0) t.entry.number.toString() else ""
                holder.number.visibility = if (t.entry.number > 0) View.VISIBLE else View.GONE
                holder.name.text = t.entry.name
                holder.star.visibility = if (t.favorite) View.VISIBLE else View.GONE
            }
            is PosterVH -> when (tile) {
                is Tile.Media -> {
                    holder.poster.loadPoster(tile.entry.icon)
                    holder.title.text = tile.entry.name
                    holder.subtitle.visibility = View.GONE
                    holder.progress.visibility = View.GONE
                    holder.star.visibility = if (tile.favorite) View.VISIBLE else View.GONE
                    if (tile.showKind) {
                        holder.kind.visibility = View.VISIBLE
                        holder.kind.text = when (tile.entry.kind) {
                            Kind.MOVIE -> "Film"
                            Kind.SERIES -> "Dizi"
                            Kind.LIVE -> "Canlı"
                        }
                    } else {
                        holder.kind.visibility = View.GONE
                    }
                }
                is Tile.Resume -> {
                    val p = tile.progress
                    holder.poster.loadPoster(p.image)
                    holder.title.text = p.title
                    holder.subtitle.visibility = View.VISIBLE
                    holder.subtitle.text = when {
                        p.kind == Kind.SERIES && p.positionMs <= 0 -> "Sıradaki: S${p.season} B${p.episode}"
                        p.kind == Kind.SERIES -> "S${p.season} B${p.episode} · ${formatTime(p.positionMs)}"
                        else -> formatTime(p.positionMs)
                    }
                    holder.kind.visibility = View.VISIBLE
                    holder.kind.text = if (p.kind == Kind.SERIES) "Dizi" else "Film"
                    holder.star.visibility = View.GONE
                    if (p.durationMs > 0 && p.positionMs > 0) {
                        holder.progress.visibility = View.VISIBLE
                        holder.progress.progress = (p.fraction * 1000).toInt()
                    } else {
                        holder.progress.visibility = View.GONE
                    }
                }
            }
        }
    }

    class LiveVH(v: View) : RecyclerView.ViewHolder(v) {
        val logo: ImageView = v.findViewById(R.id.logo)
        val number: TextView = v.findViewById(R.id.number)
        val name: TextView = v.findViewById(R.id.name)
        val star: TextView = v.findViewById(R.id.star)
    }

    class PosterVH(v: View) : RecyclerView.ViewHolder(v) {
        val poster: ImageView = v.findViewById(R.id.poster)
        val title: TextView = v.findViewById(R.id.title)
        val subtitle: TextView = v.findViewById(R.id.subtitle)
        val progress: ProgressBar = v.findViewById(R.id.progress)
        val star: TextView = v.findViewById(R.id.star)
        val kind: TextView = v.findViewById(R.id.kindBadge)
    }

    companion object {
        const val TYPE_LIVE = 1
        const val TYPE_POSTER = 2
    }
}

/** Channel list inside the player. */
class ChannelRowAdapter(
    private val onClick: (Int) -> Unit,
    private val onLongClick: (Int) -> Unit
) : RecyclerView.Adapter<ChannelRowAdapter.VH>() {

    var items: List<MediaEntry> = emptyList()
    var current: Int = -1
    var favorites: Set<String> = emptySet()

    class VH(v: View) : RecyclerView.ViewHolder(v) {
        val number: TextView = v.findViewById(R.id.number)
        val logo: ImageView = v.findViewById(R.id.logo)
        val name: TextView = v.findViewById(R.id.name)
        val star: TextView = v.findViewById(R.id.star)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val holder = VH(LayoutInflater.from(parent.context).inflate(R.layout.item_channel_row, parent, false))
        holder.itemView.setOnClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onClick(p)
        }
        holder.itemView.setOnLongClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onLongClick(p)
            true
        }
        return holder
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: VH, position: Int) {
        val e = items[position]
        holder.number.text = if (e.number > 0) e.number.toString() else (position + 1).toString()
        holder.logo.loadLogo(e.icon)
        holder.name.text = e.name
        holder.star.visibility = if (e.key in favorites) View.VISIBLE else View.GONE
        holder.itemView.isActivated = position == current
    }
}

/** Episode rows on the series screen. */
class EpisodeAdapter(
    private val onClick: (Episode) -> Unit
) : RecyclerView.Adapter<EpisodeAdapter.VH>() {

    var items: List<Episode> = emptyList()
        private set

    fun submit(list: List<Episode>) {
        items = list
        notifyDataSetChanged()
    }

    class VH(v: View) : RecyclerView.ViewHolder(v) {
        val number: TextView = v.findViewById(R.id.number)
        val title: TextView = v.findViewById(R.id.title)
        val meta: TextView = v.findViewById(R.id.meta)
        val progress: ProgressBar = v.findViewById(R.id.progress)
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val holder = VH(LayoutInflater.from(parent.context).inflate(R.layout.item_episode, parent, false))
        holder.itemView.setOnClickListener {
            val p = holder.bindingAdapterPosition
            if (p != RecyclerView.NO_POSITION) onClick(items[p])
        }
        holder.itemView.focusScale(1.02f)
        return holder
    }

    override fun getItemCount(): Int = items.size

    override fun onBindViewHolder(holder: VH, position: Int) {
        val ep = items[position]
        val store = Repo.store
        holder.number.text = "B${ep.number}"
        holder.title.text = ep.title
        val parts = mutableListOf<String>()
        if (ep.durationSec > 0) parts += "${(ep.durationSec + 59) / 60} dk"
        val watched = store.isWatched(ep.id)
        val pos = store.episodePosition(ep.id)
        if (watched) parts += "İzlendi" else if (pos > 0) parts += "Kaldığın yer ${formatTime(pos)}"
        holder.meta.text = parts.joinToString("  ·  ")
        val durMs = ep.durationSec * 1000
        if (!watched && pos > 0 && durMs > 0) {
            holder.progress.visibility = View.VISIBLE
            holder.progress.progress = ((pos.toFloat() / durMs) * 1000).toInt().coerceIn(0, 1000)
        } else {
            holder.progress.visibility = View.GONE
        }
    }
}
