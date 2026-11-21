package com.musichub.mobile.ui

import android.content.Context
import android.widget.TextView
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.musichub.mobile.R
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Locale

/**
 * On-device regression tests for B-035.
 *
 * <para>
 * Choosing a language persisted `language_tag` but never applied it: `LocaleHelper.setLanguage`
 * only wrote SharedPreferences, and the locale is applied in `attachBaseContext`, which runs once
 * per process. Tapping "fa" wrote the value while the whole UI stayed English, and even a cold
 * restart still showed English until the Activity was recreated on the main dispatcher.
 * </para>
 *
 * <para>
 * These tests assert the two halves of that contract separately so a future regression names the
 * broken half: (1) the tag is stored, (2) a fresh Activity started after the tag is stored resolves
 * Persian resources and a right-to-left layout direction.
 * </para>
 */
@RunWith(AndroidJUnit4::class)
class LocaleApplicationInstrumentedTest {

    private lateinit var context: Context

    @Before
    fun setUp() {
        context = InstrumentationRegistry.getInstrumentation().targetContext
        LocaleHelper.setLanguage(context, "")
        Locale.setDefault(Locale.US)
    }

    @After
    fun tearDown() {
        LocaleHelper.setLanguage(context, "")
        Locale.setDefault(Locale.US)
    }

    @Test
    fun setLanguage_persistsTheTag() {
        LocaleHelper.setLanguage(context, "fa")
        assertEquals("fa", LocaleHelper.currentTag(context))

        LocaleHelper.setLanguage(context, "en")
        assertEquals("en", LocaleHelper.currentTag(context))
    }

    @Test
    fun blankTag_wrapReturnsTheSameContext() {
        LocaleHelper.setLanguage(context, "")
        val wrapped = LocaleHelper.wrap(context)
        assertTrue(
            "An unset language must not swap the Context (it would fight the system locale)",
            wrapped === context,
        )
    }

    @Test
    fun persianTag_resolvesPersianResources() {
        LocaleHelper.setLanguage(context, "fa")
        val wrapped = LocaleHelper.wrap(context)

        val persian = context.getString(com.musichub.mobile.R.string.nav_home) // baseline, system locale
        val localized = wrapped.getString(com.musichub.mobile.R.string.nav_home)

        // The Persian value for nav_home is "خانه".
        assertEquals("\u062E\u0627\u0646\u0647", localized)
        assertTrue(
            "Persian resources must differ from the system-locale resources; got '$persian' for both",
            persian != localized,
        )
    }

    @Test
    fun persianTag_setsRightToLeftLayoutDirection() {
        LocaleHelper.setLanguage(context, "fa")
        val wrapped = LocaleHelper.wrap(context)

        assertEquals(
            "Persian must produce an RTL layout direction, otherwise the whole UI mirrors wrongly",
            android.view.View.LAYOUT_DIRECTION_RTL,
            wrapped.resources.configuration.layoutDirection,
        )
    }

    @Test
    fun englishTag_setsLeftToRightLayoutDirection() {
        LocaleHelper.setLanguage(context, "en")
        val wrapped = LocaleHelper.wrap(context)

        assertEquals(
            android.view.View.LAYOUT_DIRECTION_LTR,
            wrapped.resources.configuration.layoutDirection,
        )
        assertEquals("Home", wrapped.getString(com.musichub.mobile.R.string.nav_home))
    }

    /**
     * The actual B-035 regression: after the tag changes, a *newly started* Activity must render
     * Persian. This is what the Activity recreation in `LanguageRow` is for; before the fix, the
     * stored tag had no effect on the rendered UI.
     */
    @Test
    fun startingAnActivityAfterChangingLanguage_rendersTheNewLanguage() {
        // Stage 1: English selected, an Activity rendered in English.
        LocaleHelper.setLanguage(context, "en")
        ActivityScenario.launch(LocaleTestActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                val text = activity.findViewById<TextView>(R.id.locale_probe_text)
                assertEquals("Home", text.text.toString())
            }
        }

        // Stage 2: switch to Persian, as SettingsScreen does before calling recreate().
        LocaleHelper.setLanguage(context, "fa")
        ActivityScenario.launch(LocaleTestActivity::class.java).use { scenario ->
            scenario.onActivity { activity ->
                val text = activity.findViewById<TextView>(R.id.locale_probe_text)
                assertEquals(
                    "After switching to fa, an Activity started afterwards must render Persian",
                    "\u062E\u0627\u0646\u0647",
                    text.text.toString(),
                )
                assertEquals(
                    android.view.View.LAYOUT_DIRECTION_RTL,
                    activity.resources.configuration.layoutDirection,
                )
            }
        }
    }

    /**
     * Guards the persistence half of B-035 across process death: the tag must survive in a plain
     * SharedPreferences file so `attachBaseContext` can read it synchronously at startup.
     */
    @Test
    fun languageSurvivesAcrossActivityInstances() {
        LocaleHelper.setLanguage(context, "fa")

        ActivityScenario.launch(LocaleTestActivity::class.java).use { scenario ->
            scenario.onActivity { first ->
                assertEquals("\u062E\u0627\u0646\u0647", first.getString(com.musichub.mobile.R.string.nav_home))
            }
        }

        ActivityScenario.launch(LocaleTestActivity::class.java).use { scenario ->
            scenario.onActivity { second ->
                assertEquals("\u062E\u0627\u0646\u0647", second.getString(com.musichub.mobile.R.string.nav_home))
                assertEquals("fa", LocaleHelper.currentTag(second))
            }
        }
    }
}
