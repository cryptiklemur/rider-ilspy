rootProject.name = "rider-ilspy"

pluginManagement {
    val rdVersion: String by settings
    val rdKotlinVersion: String by settings

    repositories {
        gradlePluginPortal()
        mavenCentral()
        maven("https://www.jetbrains.com/intellij-repository/releases")
        maven("https://www.jetbrains.com/intellij-repository/snapshots")
        maven("https://cache-redirector.jetbrains.com/intellij-dependencies")
    }

    plugins {
        id("com.jetbrains.rdgen") version rdVersion
    }

    // The rd-gen artifact publishes a gradle marker under the id "com.jetbrains.rdgen",
    // but the legacy plugin id used by JetBrains samples is "rdgen". Map both to the
    // actual com.jetbrains.rd:rd-gen module so either spelling resolves.
    resolutionStrategy {
        eachPlugin {
            if (requested.id.name == "rdgen") {
                useModule("com.jetbrains.rd:rd-gen:$rdVersion")
            }
        }
    }
}

plugins {
    id("org.gradle.toolchains.foojay-resolver-convention") version "1.0.0"
}

dependencyResolutionManagement {
    repositories {
        mavenCentral()
        maven("https://www.jetbrains.com/intellij-repository/releases")
        maven("https://www.jetbrains.com/intellij-repository/snapshots")
        maven("https://cache-redirector.jetbrains.com/intellij-dependencies")
    }
}

include(":protocol")
