package com.musichub.mobile

import android.app.Application
import com.musichub.mobile.data.AppContainer

class MusicHubApp : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}
