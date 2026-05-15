package com.cryptiklemur.riderilspy.internals

/**
 * Path-only equivalent of [IlSpyModeStatusBarWidget.isIlSpyDecompiledFile].
 *
 * Lives at top level so it can be unit-tested without spinning up an IDE
 * VirtualFile. The widget delegates to this with `file.path`.
 *
 * Match condition: the path passes through the ILSpy decompile cache
 * directory tree (`DecompilerCache/RiderIlSpy`). Both Unix (`/`) and Windows
 * (`\`) separators are accepted so the check works regardless of the host OS
 * that produced the [com.intellij.openapi.vfs.VirtualFile] backing.
 */
internal fun isIlSpyDecompiledPath(path: String): Boolean =
    path.contains("/DecompilerCache/RiderIlSpy/") || path.contains("\\DecompilerCache\\RiderIlSpy\\")
