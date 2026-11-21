package com.musichub.mobile.ui

import android.content.Context
import android.os.Bundle
import android.widget.TextView
import androidx.activity.ComponentActivity
import com.musichub.mobile.R

/**
 * Debug-only probe Activity that reproduces `MainActivity`'s locale seam exactly: the locale is
 * applied in `attachBaseContext`, before any resource is resolved.
 *
 * <para>
 * It lives in the `debug` source set rather than `androidTest` because it must run in the
 * application process. A test-APK Activity resolves against `com.musichub.mobile.test` and
 * `ActivityScenario.launch` rejects it with "Intent in process com.musichub.mobile resolved to
 * different process". It is never compiled into a release build.
 * </para>
 */
class LocaleTestActivity : ComponentActivity() {
    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // A plain View keeps the assertion independent of Compose recomposition.
        setContentView(
            TextView(this).apply {
                id = R.id.locale_probe_text
                text = getString(R.string.nav_home)
            },
        )
    }
}
