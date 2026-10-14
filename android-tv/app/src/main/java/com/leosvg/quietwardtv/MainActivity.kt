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
                    """(function(){
                      var m=document.querySelector('meta[name=viewport]');
                      if(m)m.setAttribute('content','width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no');
                      document.documentElement.style.overflow='hidden';document.body.style.overflow='hidden';
                      var fitStyle=document.getElementById('tv-screen-fit-style');
                      if(!fitStyle){fitStyle=document.createElement('style');fitStyle.id='tv-screen-fit-style';document.head.appendChild(fitStyle);}
                      fitStyle.textContent=
                        '@media (orientation:landscape) and (max-height:800px){'+
                        '#intro.screen{padding:8px 16px;overflow-x:hidden;overflow-y:auto;place-items:center;}'+
                        '#intro .panel{width:min(570px,100%);max-height:calc(100vh - 16px);max-height:calc(100dvh - 16px);overflow-y:auto;overscroll-behavior:contain;box-sizing:border-box;padding:8px 12px;scrollbar-width:thin;}'+
                        '#intro .eyebrow{font-size:clamp(8px,1.5vh,10px);}'+
                        '#intro .rule{margin:clamp(7px,1.4vh,12px) auto;}'+
                        '#intro h1{font-size:clamp(30px,6.7vh,46px);line-height:.98;}'+
                        '#intro .subtitle{margin-top:6px;font-size:9px;}'+
                        '#intro .copy{margin:clamp(8px,1.5vh,13px) auto;font-size:clamp(12px,2.2vh,14px);line-height:1.35;}'+
                        '#intro .instructions{gap:4px 14px;margin:clamp(8px,1.5vh,13px) auto;font-size:clamp(8px,1.5vh,9px);line-height:1.4;}'+
                        '#intro .lobby-actions{gap:6px;margin:5px 0;}'+
                        '#intro .start{padding:8px 12px;font-size:9px;letter-spacing:.12em;}'+
                        '#intro .footnote{margin-top:8px;font-size:8px;line-height:1.4;}'+
                        '}@media(max-height:500px){#intro .panel{padding-top:4px;padding-bottom:4px;}#intro .copy{margin:6px auto;}#intro .instructions{margin:6px auto;}#intro .footnote{margin-top:5px;}}';

                      if(!document.getElementById('tv-controller-guide')){
                        var style=document.createElement('style');
                        style.id='tv-controller-guide-style';
                        style.textContent='#tv-controller-guide{position:fixed;z-index:2147483647;right:12px;top:12px;max-width:250px;padding:10px 12px;background:rgba(5,9,12,.92);border:1px solid #c5ae76;color:#f0dfb6;font:12px/1.5 monospace;box-shadow:0 4px 18px #000;pointer-events:none}#tv-controller-guide strong{display:block;font-size:13px;margin-bottom:4px}#tv-controller-guide .muted{color:#d0d0c0}';
                        document.head.appendChild(style);
                        var guide=document.createElement('div');guide.id='tv-controller-guide';
                        guide.innerHTML='<strong>CONTROLLER GUIDE · SELECT to hide/show</strong><div>Left stick / D-pad: Move and turn</div><div>A / Y / L2: Start or interact</div><div>X / R2: Use weapon</div><div>B / START: Back / escape hiding</div><div>L1 / R1: Sprint (hold)</div><div class="muted">Right stick: Move/turn</div>';
                        document.body.appendChild(guide);
                      }
                      // Fallback for controllers exposed by Chromium's Gamepad API but not as Android key events.
                      if(!window.__quietWardGamepadPoller){
                        var gpState=window.__quietWardGamepadPoller={held:{},buttons:{},lastAction:0};
                        var gpMap={12:['ArrowUp','ArrowUp'],13:['ArrowDown','ArrowDown'],14:['ArrowLeft','ArrowLeft'],15:['ArrowRight','ArrowRight'],1:['Escape','Escape'],3:['e','KeyE'],4:['Shift','ShiftLeft'],5:['Shift','ShiftRight'],6:['Control','ControlLeft'],8:['Tab','Tab'],9:['Escape','Escape']};
                        function gpAction(action){
                          var now=Date.now();if(now-gpState.lastAction<220)return;gpState.lastAction=now;
                          var intro=document.querySelector('#intro'),start=document.querySelector('#start');
                          if(action==='interact'&&intro&&!intro.classList.contains('hidden')&&start){start.click();return;}
                          if(window.__quietWardTvAction)window.__quietWardTvAction(action);
                        }
                        function gpEvent(key,code,down,id){
                          if(!!gpState.held[id]===down)return;
                          gpState.held[id]=down;
                          window.dispatchEvent(new KeyboardEvent(down?'keydown':'keyup',{key:key,code:code,bubbles:true,cancelable:true,repeat:false}));
                        }
                        window.__quietWardPollGamepads=function(){
                          try{
                            var pads=navigator.getGamepads?navigator.getGamepads():[];
                            var nativeRecent=Date.now()-(window.__quietWardNativeInputAt||0)<500;
                            for(var p=0;p<pads.length;p++){
                              var pad=pads[p];if(!pad||!pad.connected)continue;
                              for(var b=0;b<pad.buttons.length;b++){
                                var pressed=!!(pad.buttons[b]&&(pad.buttons[b].pressed||pad.buttons[b].value>.55));
                                var id='gp'+p+'b'+b,was=!!gpState.buttons[id];gpState.buttons[id]=pressed;
                                if(!nativeRecent&&pressed&&!was){
                                  if(b===0)gpAction('interact');
                                  else if(b===2||b===7)gpAction('weapon');
                                  else if(gpMap[b])gpEvent(gpMap[b][0],gpMap[b][1],true,id);
                                }else if(!nativeRecent&&!pressed&&was&&gpMap[b])gpEvent(gpMap[b][0],gpMap[b][1],false,id);
                              }
                              var axes=pad.axes||[],lx=axes[0]||0,ly=axes[1]||0,rx=axes.length>2?axes[2]:0;
                              var want={'ArrowUp':ly<-.35,'ArrowDown':ly>.35,'ArrowLeft':lx<-.35||rx<-.45,'ArrowRight':lx>.35||rx>.45};
                              for(var key in want){var id='axis'+p+key,was=!!gpState.held[id];if(!nativeRecent&&want[key]!==was)gpEvent(key,key,want[key],id);}
                            }
                          }catch(e){}
                          requestAnimationFrame(window.__quietWardPollGamepads);
                        };
                        requestAnimationFrame(window.__quietWardPollGamepads);
                      }
                      window.__quietWardTvAction=function(action){
                        var intro=document.querySelector('#intro'),start=document.querySelector('#start');
                        if(action==='interact'&&intro&&!intro.classList.contains('hidden')&&start){start.click();return;}
                        var canvas=document.querySelector('canvas');
                        if(canvas){
                          var right=action==='weapon';
                          canvas.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,pointerId:1,pointerType:'mouse',button:right?2:0,buttons:right?2:1,clientX:innerWidth/2,clientY:innerHeight/2}));
                        }
                      };
                      window.addEventListener('keydown',function(e){
                        if(e.code==='Tab'){e.preventDefault();var g=document.getElementById('tv-controller-guide');if(g)g.style.display=g.style.display==='none'?'block':'none';}
                        if(e.code==='ControlLeft'||e.code==='AltRight'){if(!e.repeat&&window.__quietWardTvAction)window.__quietWardTvAction(e.code==='ControlLeft'?'interact':'weapon');}
                      });
                    })();""",
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
            val js = "(function(){if('$type'==='keydown' && '$key'==='Enter'){const intro=document.querySelector('#intro');if(intro && !intro.classList.contains('hidden')){const start=document.querySelector('#start');if(start){start.click();return;}}const a=document.activeElement;if(a && a!==document.body && (a.tagName==='BUTTON'||a.tagName==='INPUT'||a.tagName==='SELECT'||a.getAttribute('role')==='button')){if(typeof a.click==='function'){a.click();return;}}}if('$type'==='keydown' && '$code'==='Enter' && window.__quietWardTvAction){window.__quietWardTvAction('interact');return;}if('$type'==='keydown' && '$code'==='KeyE' && window.__quietWardTvAction){window.__quietWardTvAction('interact');return;}if('$type'==='keydown' && '$code'==='Space' && window.__quietWardTvAction){window.__quietWardTvAction('weapon');return;}const e=new KeyboardEvent('$type',{key:'$key',code:'$code',bubbles:true,cancelable:true,repeat:$repeat});window.dispatchEvent(e);})();"
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
            val rx = event.getAxisValue(MotionEvent.AXIS_RX)
            val ry = event.getAxisValue(MotionEvent.AXIS_RY)
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
