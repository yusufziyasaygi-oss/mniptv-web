package com.mavinokta.iptv.tv.ui

import android.view.FocusFinder
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView

/** True when this view is [parent] or one of its descendants. */
internal fun View.isInside(parent: View): Boolean {
    var v: View? = this
    while (v != null) {
        if (v === parent) return true
        v = v.parent as? View
    }
    return false
}

/** Next focusable inside [root] in [direction], or null when the edge is reached. */
internal fun nextFocusInside(root: ViewGroup, focused: View, direction: Int): View? =
    FocusFinder.getInstance().findNextFocus(root, focused, direction)

/** Moves focus to the row at [index]; scrolls first when the row is not laid out yet. */
internal fun RecyclerView.focusRow(index: Int): Boolean {
    val count = adapter?.itemCount ?: 0
    if (count == 0) return false
    val i = index.coerceIn(0, count - 1)
    val holder = findViewHolderForAdapterPosition(i)
    if (holder != null) {
        holder.itemView.requestFocus()
        return true
    }
    scrollToPosition(i)
    post {
        val h = findViewHolderForAdapterPosition(i)
        if (h != null) h.itemView.requestFocus() else getChildAt(0)?.requestFocus()
    }
    return true
}

/** Focuses the first visible row (or the first row). */
internal fun RecyclerView.focusFirstVisible(): Boolean {
    val lm = layoutManager as? LinearLayoutManager
    val first = lm?.findFirstCompletelyVisibleItemPosition()?.takeIf { it >= 0 }
        ?: lm?.findFirstVisibleItemPosition()?.takeIf { it >= 0 }
        ?: 0
    return focusRow(first)
}
