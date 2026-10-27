plugins { id("com.android.application") }

android {
    namespace = "com.cavwic.s2n"
    compileSdk = 36
    defaultConfig {
        applicationId = "com.cavwic.s2n"
        minSdk = 26
        targetSdk = 36
        versionCode = 40004
        versionName = "0.4.0-beta.4"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") }
    }
    val releaseKey = System.getenv("ANDROID_KEYSTORE_PATH")
    if (!releaseKey.isNullOrBlank()) {
        signingConfigs.create("distribution") {
            storeFile = file(releaseKey)
            storeType = "PKCS12"
            storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
            keyAlias = System.getenv("ANDROID_KEY_ALIAS")
            keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
        }
    }
    buildTypes {
        debug { applicationIdSuffix = ".debug" }
        release {
            isMinifyEnabled = false
            isDebuggable = false
            if (!releaseKey.isNullOrBlank()) signingConfig = signingConfigs.getByName("distribution")
        }
    }
    buildFeatures { buildConfig = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    testOptions { unitTests.isReturnDefaultValues = true }
    packaging { jniLibs.useLegacyPackaging = false }
    lint { abortOnError = true }
}

// Distribution builds must never silently use a debug key or produce an unsigned APK.
tasks.matching { it.name == "validateSigningRelease" || it.name == "packageRelease" }.configureEach {
    doFirst {
        check(!System.getenv("ANDROID_KEYSTORE_PATH").isNullOrBlank()) { "Release signing key is required" }
    }
}

dependencies {
    implementation(files("libs/libbox.aar"))
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20250517")
    androidTestImplementation("androidx.test:runner:1.7.0")
    androidTestImplementation("androidx.test:rules:1.7.0")
    androidTestImplementation("androidx.test.ext:junit:1.3.0")
    androidTestImplementation("androidx.test.uiautomator:uiautomator:2.3.0")
}
