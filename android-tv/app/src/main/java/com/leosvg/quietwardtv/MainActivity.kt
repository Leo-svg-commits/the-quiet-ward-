package com.leosvg.quietwardtv

import android.app.Activity
import android.os.Bundle
import android.view.KeyEvent
import android.view.InputDevice
import android.view.MotionEvent
import android.view.View
import android.webkit.WebChromeClient
import android.webkit.WebView
import android.webkit.WebViewClient

class MainActivity : Activity() {
    private lateinit var gameView: WebView
    private val gameUrl = "https://quiet-ward-test.onrender.com/"
    private val joystickKeys = mutableSetOf<String>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_FULLSCREEN or
            View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
            View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
            View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
        )

        gameView = WebView(this)
        gameView.isFocusable = true
        gameView.isFocusableInTouchMode = true
        gameView.setBackgroundColor(android.graphics.Color.BLACK)
        gameView.settings.javaScriptEnabled = true
        gameView.settings.domStorageEnabled = true
        gameView.settings.databaseEnabled = true
        gameView.settings.loadsImagesAutomatically = true
        gameView.settings.mediaPlaybackRequiresUserGesture = false
        gameView.settings.useWideViewPort = true
        gameView.settings.loadWithOverviewMode = true
        gameView.setInitialScale(65)
        gameView.webChromeClient = WebChromeClient()
        gameView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                gameView.evaluateJavascript(
                    "(function(){var m=document.querySelector('meta[name=viewport]');if(m){m.setAttribute('content','width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no');}document.documentElement.style.overflow='hidden';document.body.style.overflow='hidden';})();",
                    null
                )
                gameView.requestFocus()
            }
        }
        setContentView(gameView)
        gameView.loadUrl(gameUrl)
    }

    private fun mappedKey(keyCode: Int): Pair<String, String>? = when (keyCode) {
        KeyEvent.KEYCODE_DPAD_UP -> "ArrowUp" to "ArrowUp"
        KeyEvent.KEYCODE_DPAD_DOWN -> "ArrowDown" to "ArrowDown"
        KeyEvent.KEYCODE_DPAD_LEFT -> "ArrowLeft" to "ArrowLeft"
        KeyEvent.KEYCODE_DPAD_RIGHT -> "ArrowRight" to "ArrowRight"
        KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_BUTTON_A -> "Enter" to "Enter"
        KeyEvent.KEYCODE_BUTTON_B -> "Escape" to "Escape"
        KeyEvent.KEYCODE_BUTTON_X -> " " to "Space"
        KeyEvent.KEYCODE_BUTTON_Y -> "e" to "KeyE"
        KeyEvent.KEYCODE_BUTTON_L1 -> "Shift" to "ShiftLeft"
        KeyEvent.KEYCODE_BUTTON_R1 -> "Shift" to "ShiftRight"
        KeyEvent.KEYCODE_BUTTON_L2 -> "Control" to "ControlLeft"
        KeyEvent.KEYCODE_BUTTON_R2 -> "Alt" to "AltRight"
        KeyEvent.KEYCODE_BUTTON_START -> "Escape" to "Escape"
        KeyEvent.KEYCODE_BUTTON_SELECT -> "Tab" to "Tab"
        KeyEvent.KEYCODE_SPACE -> " " to "Space"
        KeyEvent.KEYCODE_ESCAPE -> "Escape" to "Escape"
        KeyEvent.KEYCODE_W -> "w" to "KeyW"
        KeyEvent.KEYCODE_A -> "a" to "KeyA"
        KeyEvent.KEYCODE_S -> "s" to "KeyS"
        KeyEvent.KEYCODE_D -> "d" to "KeyD"
        else -> null
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        val mapped = mappedKey(event.keyCode)
        if (mapped != null && (event.action == KeyEvent.ACTION_DOWN || event.action == KeyEvent.ACTION_UP)) {
            val type = if (event.action == KeyEvent.ACTION_DOWN) "keydown" else "keyup"
            val key = mapped.first.replace("\\", "\\\\").replace("'", "\\'")
            val code = mapped.second.replace("'", "\\'")
            val repeat = if (event.repeatCount > 0) "true" else "false"
            val js = "(function(){if('$type'==='keydown' && '$key'==='Enter'){const intro=document.querySelector('#intro');if(intro && !intro.classList.contains('hidden')){const start=document.querySelector('#start');if(start){start.click();return;}}const a=document.activeElement;if(a && a!==document.body && (a.tagName==='BUTTON'||a.tagName==='INPUT'||a.tagName==='SELECT'||a.getAttribute('role')==='button')){if(typeof a.click==='function'){a.click();return;}}}const e=new KeyboardEvent('$type',{key:'$key',code:'$code',bubbles:true,cancelable:true,repeat:$repeat});window.dispatchEvent(e);})();"
            runOnUiThread { gameView.evaluateJavascript(js, null) }
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    private fun sendKey(key: String, code: String, down: Boolean) {
        val type = if (down) "keydown" else "keyup"
        val safeKey = key.replace("\\", "\\\\").replace("'", "\\'")
        val safeCode = code.replace("'", "\\'")
        val js = "window.dispatchEvent(new KeyboardEvent('$type',{key:'$safeKey',code:'$safeCode',bubbles:true,cancelable:true}));"
        runOnUiThread { if (::gameView.isInitialized) gameView.evaluateJavascript(js, null) }
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean {
        if (event.action == MotionEvent.ACTION_MOVE &&
            (event.source and InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK) {
            val x = event.getAxisValue(MotionEvent.AXIS_X)
            val y = event.getAxisValue(MotionEvent.AXIS_Y)
            val rx = event.getAxisValue(MotionEvent.AXIS_Z)
            val ry = event.getAxisValue(MotionEvent.AXIS_RZ)
            val wanted = mutableSetOf<String>()
            if (y < -0.45f || ry < -0.65f) wanted.add("ArrowUp")
            if (y > 0.45f || ry > 0.65f) wanted.add("ArrowDown")
            if (x < -0.45f || rx < -0.45f) wanted.add("ArrowLeft")
            if (x > 0.45f || rx > 0.45f) wanted.add("ArrowRight")
            val map = mapOf("ArrowUp" to "ArrowUp", "ArrowDown" to "ArrowDown",
                "ArrowLeft" to "ArrowLeft", "ArrowRight" to "ArrowRight")
            for (key in joystickKeys.toSet()) {
                if (key !in wanted) sendKey(key, map[key] ?: key, false)
            }
            for (key in wanted) {
                if (key !in joystickKeys) sendKey(key, map[key] ?: key, true)
            }
            joystickKeys.clear()
            joystickKeys.addAll(wanted)
            return true
        }
        return super.onGenericMotionEvent(event)
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        if (::gameView.isInitialized && gameView.canGoBack()) gameView.goBack()
        else super.onBackPressed()
    }
}
