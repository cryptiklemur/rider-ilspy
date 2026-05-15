import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType
import org.jetbrains.intellij.platform.gradle.TestFrameworkType
import org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask
import org.jetbrains.intellij.platform.gradle.tasks.RunIdeTask
import org.jetbrains.intellij.platform.gradle.tasks.VerifyPluginTask

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
    compilerOptions {
        jvmDefault.set(org.jetbrains.kotlin.gradle.dsl.JvmDefaultMode.NO_COMPATIBILITY)
    }
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
    testImplementation(platform("org.junit:junit-bom:5.11.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
    // junit-vintage transitively pulls JUnit 3/4's junit.framework.TestCase, which
    // IntelliJ's bundled JUnit5TestSessionListener references during initialization.
    testRuntimeOnly("org.junit.vintage:junit-vintage-engine")
    testRuntimeOnly("junit:junit:4.13.2")
}

intellijPlatform {
    projectName = providers.gradleProperty("pluginName")

    pluginConfiguration {
        ideaVersion {
            sinceBuild = "261"
            untilBuild = provider { null }
        }
    }

    publishing {
        token = providers.environmentVariable("JETBRAINS_MARKETPLACE_TOKEN")
    }

    pluginVerification {
        ides {
            // current stable major (our build target)
            create(IntelliJPlatformType.Rider, "2026.1.1") {
                useInstaller = false
            }
            // prior stable major
            create(IntelliJPlatformType.Rider, "2025.3.4.1") {
                useInstaller = false
            }
            // current EAP cycle — pinned EAP1 + rolling latest of the 2026.2 dev line
            create(IntelliJPlatformType.Rider, "2026.2-EAP1-SNAPSHOT") {
                useInstaller = false
            }
            create(IntelliJPlatformType.Rider, "2026.2-SNAPSHOT") {
                useInstaller = false
            }
        }
        failureLevel = listOf(
            VerifyPluginTask.FailureLevel.COMPATIBILITY_PROBLEMS,
            VerifyPluginTask.FailureLevel.INVALID_PLUGIN,
            VerifyPluginTask.FailureLevel.MISSING_DEPENDENCIES,
            VerifyPluginTask.FailureLevel.PLUGIN_STRUCTURE_WARNINGS,
        )
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

tasks.withType<Test>().configureEach {
    useJUnitPlatform {
        // The IntelliJ Platform test framework registers a JUnit5 session listener whose
        // dependencies require running inside a TestApplicationManager; plain unit tests
        // do not boot the platform, so filter it out at the launcher level.
        excludeEngines("intellij")
    }
    systemProperty("idea.force.use.core.classloader", "true")
    jvmArgs(
        "--add-opens=java.base/java.lang=ALL-UNNAMED",
        "--add-opens=java.base/java.util=ALL-UNNAMED",
    )
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

