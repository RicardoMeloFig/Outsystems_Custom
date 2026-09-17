import OSFirebaseMessagingLib

extension OSFCMClickableType: CustomStringConvertible {
    public var description: String {
        return switch self {
        case .notification(latestVersion: let latestVersion):
            "notificationClick\(latestVersion ? "V2" : "")"
        case .action:
            "internalRouteActionClick"
        @unknown default:
            preconditionFailure("Not supposed to get here")
        }
    }
}

extension FirebaseNotificationType: CustomStringConvertible {
    public var description: String { "\(self == .silentNotification ? "silent": "default")Notification" }
}
