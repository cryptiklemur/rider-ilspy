import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType
import org.jetbrains.intellij.platform.gradle.TestFrameworkType
import org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask
import org.jetbrains.intellij.platform.gradle.tasks.RunIdeTask

plugins {
    kotlin("jvm") version "2.2.0"
    id("org.jetbrains.intellij.platform") version "2.16.0"
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

val platformType: String = providers.gradleProperty("platformType").get()
val platformVersion: String = providers.gradleProperty("platformVersion").get()

kotlin {
    jvmToolchain(21)
}

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        rider(platformVersion) {
            useInstaller = false
        }
        jetbrainsRuntime()
        testFramework(TestFrameworkType.Platform)
    }
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = "261"
            untilBuild = provider { null }
        }
    }

    publishing {
        token = providers.environmentVariable("JETBRAINS_MARKETPLACE_TOKEN")
    }
}

val dotNetSrcDir = layout.projectDirectory.dir("ReSharperPlugin")
val resharperPluginBin = dotNetSrcDir.dir("RiderIlSpy/bin/Release")
val outputAssemblyName = "RiderIlSpy"

val buildReSharperPlugin by tasks.registering(Exec::class) {
    group = "build"
    description = "Build the .NET ReSharper backend plugin"
    workingDir = dotNetSrcDir.asFile
    commandLine("dotnet", "build", "RiderIlSpy.sln", "-c", "Release")
    inputs.dir(dotNetSrcDir)
    outputs.dir(resharperPluginBin)
}

tasks.named("buildPlugin") {
    dependsOn(buildReSharperPlugin)
}

tasks.withType<PrepareSandboxTask>().configureEach {
    dependsOn(buildReSharperPlugin)
    from(resharperPluginBin) {
        include("$outputAssemblyName.dll")
        include("$outputAssemblyName.pdb")
        into("${intellijPlatform.projectName.get()}/dotnet")
    }
}

val jbrRoot: String? = file(System.getProperty("user.home") + "/.gradle/caches/9.0.0/transforms").walkTopDown()
    .firstOrNull { it.isDirectory && it.name.startsWith("jbr_jcef-") && it.resolve("bin/java").exists() }
    ?.absolutePath

tasks.named("buildSearchableOptions") {
    enabled = false
}

tasks.withType<RunIdeTask>().configureEach {
    if (jbrRoot != null) {
        javaLauncher.set(javaToolchains.launcherFor {
            languageVersion.set(JavaLanguageVersion.of(25))
            implementation.set(JvmImplementation.VENDOR_SPECIFIC)
            vendor.set(JvmVendorSpec.JETBRAINS)
        })
        logger.lifecycle("runIde will use JBR via toolchain")
    }
}

