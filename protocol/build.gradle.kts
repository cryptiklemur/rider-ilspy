import com.jetbrains.rd.generator.gradle.RdGenTask

plugins {
    // Version comes from settings.gradle.kts → rdVersion (com.jetbrains.rd:rd-gen).
    id("com.jetbrains.rdgen")
    kotlin("jvm")
}

repositories {
    mavenCentral()
    maven("https://cache-redirector.jetbrains.com/intellij-dependencies")
}

val rdVersion: String by project
val rdKotlinVersion: String by project

dependencies {
    // rd-gen is the model generator. Pulling it in as `implementation` puts the
    // DSL base classes (Ext, Toplevel, Property, Signal, …) on the protocol
    // sourceset's compile classpath so our DSL files compile.
    implementation("com.jetbrains.rd:rd-gen:$rdVersion")
    implementation("org.jetbrains.kotlin:kotlin-stdlib:$rdKotlinVersion")
    // rider-model.jar gives us the Solution toplevel that our model hangs off of.
    // The root project exposes it as a `riderModel` Configuration so we don't
    // have to know the resolved Rider SDK path.
    implementation(project(mapOf("path" to ":", "configuration" to "riderModel")))
}

kotlin {
    jvmToolchain(21)
}

// rdgen output directories. Layout mirrors the resharper-unity / sample-rider-plugin
// convention: generated Kotlin lives in a sibling sourceset on the root project,
// generated C# lands inside the ReSharper backend project so its .cs glob picks it up.
val rootRepo: File = projectDir.parentFile
val ktOutputDir: File = rootRepo.resolve("src/main/rdgen/kotlin/com/cryptiklemur/riderilspy/model")
val csOutputDir: File = rootRepo.resolve("ReSharperPlugin/RiderIlSpy/Generated/Model")

rdgen {
    verbose = true

    generator {
        language = "kotlin"
        transform = "asis"
        // Searched for DSL toplevels under this package.
        packages = "com.cryptiklemur.riderilspy.model"
        // IdeRoot is Rider's application-scoped root; our Ext hangs off
        // SolutionModel.Solution so generating from IdeRoot picks it up
        // alongside any other Solution-scoped extensions.
        root = "com.jetbrains.rider.model.nova.ide.IdeRoot"
        directory = ktOutputDir.absolutePath
    }

    generator {
        language = "csharp"
        transform = "reversed"
        packages = "com.cryptiklemur.riderilspy.model"
        root = "com.jetbrains.rider.model.nova.ide.IdeRoot"
        directory = csOutputDir.absolutePath
    }
}

tasks.withType<RdGenTask>().configureEach {
    val classPath = sourceSets["main"].runtimeClasspath
    dependsOn(classPath)
    classpath(classPath)
}
