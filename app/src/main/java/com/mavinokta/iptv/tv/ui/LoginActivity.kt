package com.mavinokta.iptv.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.ProgressBar
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.mavinokta.iptv.tv.R
import com.mavinokta.iptv.tv.data.Account
import com.mavinokta.iptv.tv.data.Repo
import com.mavinokta.iptv.tv.data.XtreamApi
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

class LoginActivity : AppCompatActivity() {

    private lateinit var serverInput: EditText
    private lateinit var userInput: EditText
    private lateinit var passInput: EditText
    private lateinit var errorText: TextView
    private lateinit var connectButton: TextView
    private lateinit var progress: ProgressBar
    private var busy = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Repo.init(this)
        setContentView(R.layout.activity_login)

        serverInput = findViewById(R.id.serverInput)
        userInput = findViewById(R.id.userInput)
        passInput = findViewById(R.id.passInput)
        errorText = findViewById(R.id.errorText)
        connectButton = findViewById(R.id.connectButton)
        progress = findViewById(R.id.loginProgress)

        Repo.store.account?.let {
            serverInput.setText(it.server)
            userInput.setText(it.username)
            passInput.setText(it.password)
        }

        connectButton.setOnClickListener { connect() }
        passInput.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) {
                hideKeyboard(passInput)
                connectButton.requestFocus()
                true
            } else {
                false
            }
        }

        if (serverInput.text.isNullOrBlank()) serverInput.requestFocus() else connectButton.requestFocus()
    }

    private fun hideKeyboard(view: View) {
        val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as? InputMethodManager
        imm?.hideSoftInputFromWindow(view.windowToken, 0)
    }

    private fun showError(message: String?) {
        if (message.isNullOrBlank()) {
            errorText.visibility = View.GONE
        } else {
            errorText.text = message
            errorText.visibility = View.VISIBLE
        }
    }

    private fun connect() {
        if (busy) return
        val server = Account.normalizeServer(serverInput.text.toString())
        val user = userInput.text.toString().trim()
        val pass = passInput.text.toString().trim()
        if (server.isEmpty() || user.isEmpty() || pass.isEmpty()) {
            showError("Sunucu adresi, kullanıcı adı ve şifre gerekli")
            return
        }
        hideKeyboard(connectButton)
        showError(null)
        busy = true
        progress.visibility = View.VISIBLE
        connectButton.isEnabled = false

        val account = Account(server, user, pass)
        lifecycleScope.launch {
            try {
                val error = XtreamApi(account, Repo.http).authenticate()
                if (error == null) {
                    Repo.store.account = account
                    Repo.clearCache()
                    startActivity(
                        Intent(this@LoginActivity, MainActivity::class.java)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
                    )
                    finish()
                } else {
                    showError(error)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                showError("Sunucuya bağlanılamadı: ${e.message ?: e.javaClass.simpleName}")
            } finally {
                busy = false
                progress.visibility = View.GONE
                connectButton.isEnabled = true
            }
        }
    }
}
