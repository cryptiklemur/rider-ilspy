import org.jetbrains.intellij.platform.gradle.Constants
import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType
import org.jetbrains.intellij.platform.gradle.TestFrameworkType
import org.jetbrains.intellij.platform.gradle.tasks.PrepareSandboxTask
import org.jetbrains.intellij.platform.gradle.tasks.RunIdeTask
import org.jetbrains.intellij.platform.gradle.tasks.VerifyPluginTask

val rdKotlinVersion: String by project

plugins {
    // Match the Kotlin version of rider-model.jar bundled with Rider 2026.1.1
    // (metadata [2,3,0]). 2.3.21 is the latest patch on the 2.3.x line, which
    // stays binary-compatible with that metadata. The :protocol subproject's
    // DSL extends classes from rider-model.jar, so producing both sides with
    // the same compiler avoids "incompatible Kotlin metadata" failures at
    // rd-gen time.
    kotlin("jvm") version "2.3.21"
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
    testImplementation(platform("org.junit:junit-bom:5.14.4"))
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
        // Marketplace "What's New" content. Populated at release time by
        // scripts/release-notes-writer.mjs (a local semantic-release plugin
        // that converts release-notes-generator's markdown output to HTML).
        // The file is missing for local/non-release builds — the Provider
        // returns an empty string in that case so patchPluginXml stays happy.
        changeNotes = layout.buildDirectory.file("changelog-latest.html").map { file ->
            file.asFile.takeIf { it.exists() }?.readText()?.ifBlank { "" } ?: ""
        }.orElse("")
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

// Expose Rider's bundled rider-model.jar as a published Configuration so the
// :protocol subproject can consume it without having to know the on-disk path
// to the resolved Rider SDK. INITIALIZE_INTELLIJ_PLATFORM_PLUGIN is the task
// that resolves intellijPlatform.platformPath, so we wire that as the producer.
val riderModel: Configuration by configurations.creating {
    isCanBeConsumed = true
    isCanBeResolved = false
}

artifacts {
    add(riderModel.name, provider {
        intellijPlatform.platformPath.resolve("lib/rd/rider-model.jar").also {
            check(it.toFile().isFile) {
                "rider-model.jar is not found at ${it.toAbsolutePath()}"
            }
        }.toFile()
    }) {
        builtBy(Constants.Tasks.INITIALIZE_INTELLIJ_PLATFORM_PLUGIN)
    }
}

// Source dir for rd-gen kotlin output. The :protocol subproject writes to this
// path (see protocol/build.gradle.kts), so adding it here makes the generated
// model classes visible to the main plugin compile.
sourceSets {
    main {
        kotlin.srcDir(layout.projectDirectory.dir("src/main/rdgen/kotlin"))
    }
}

tasks.named("compileKotlin") {
    dependsOn(":protocol:rdgen")
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

