import PassKit

extension PKPassLibrary: OSPMTWalletAvailabilityDelegate {
    /// Verifies if the wallet is available for usage.
    /// - Returns: A boolean indicating if the wallet is available.
    static func isWalletAvailable() -> Bool {
        Self.isPassLibraryAvailable()
    }
}
