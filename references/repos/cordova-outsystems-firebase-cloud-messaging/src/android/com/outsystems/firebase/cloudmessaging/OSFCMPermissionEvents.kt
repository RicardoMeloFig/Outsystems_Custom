package com.outsystems.firebase.cloudmessaging

sealed class OSFCMPermissionEvents {
    data object Granted: OSFCMPermissionEvents()
    data object NotGranted: OSFCMPermissionEvents()
}