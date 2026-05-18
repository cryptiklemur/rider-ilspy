package com.cryptiklemur.riderilspy.i18n

import com.intellij.DynamicBundle
import org.jetbrains.annotations.Nls
import org.jetbrains.annotations.PropertyKey

/**
 * Plugin's i18n bundle. All user-facing strings resolve through this
 * accessor so we get IDE highlighting on missing keys (via @PropertyKey)
 * and so a future translator only has to drop a
 * `RiderIlSpyBundle_<lang>.properties` next to the baseline file.
 */
object RiderIlSpyBundle : DynamicBundle(BUNDLE_FQN) {
    @Nls
    fun message(@PropertyKey(resourceBundle = BUNDLE_FQN) key: String, vararg params: Any): String =
        getMessage(key, *params)
}

const val BUNDLE_FQN = "messages.RiderIlSpyBundle"
