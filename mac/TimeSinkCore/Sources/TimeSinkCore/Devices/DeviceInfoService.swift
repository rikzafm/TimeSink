import Foundation

public enum DeviceInfoService {
    public static func computeDisplayNames(
        devices: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)],
        shortNames: Bool
    ) -> [String: String] {
        var result: [String: String] = [:]
        if !shortNames {
            for d in devices {
                let fallback = d.platform.lowercased() == "windows" ? "PC" : "Mac"
                let trimmed = d.name.trimmingCharacters(in: .whitespacesAndNewlines)
                result[d.id] = !trimmed.isEmpty ? trimmed : fallback
            }
            return result
        }

        let winDevices = devices.filter { $0.platform.lowercased() == "windows" }
        let macDevices = devices.filter { $0.platform.lowercased() != "windows" }

        if winDevices.count == 1 && macDevices.count == 1 {
            result[winDevices[0].id] = "PC"
            result[macDevices[0].id] = "Mac"
            return result
        }

        // Windows devices
        if winDevices.count == 1 {
            result[winDevices[0].id] = "PC"
        } else if winDevices.count > 1 {
            let sorted = winDevices.sorted {
                if $0.isLocal != $1.isLocal { return $0.isLocal && !$1.isLocal }
                if $0.firstSeen != $1.firstSeen { return $0.firstSeen < $1.firstSeen }
                return $0.id < $1.id
            }
            for (idx, dev) in sorted.enumerated() {
                result[dev.id] = "PC \(idx + 1)"
            }
        }

        // Mac devices
        if macDevices.count == 1 {
            result[macDevices[0].id] = "Mac"
        } else if macDevices.count > 1 {
            let sorted = macDevices.sorted {
                if $0.isLocal != $1.isLocal { return $0.isLocal && !$1.isLocal }
                if $0.firstSeen != $1.firstSeen { return $0.firstSeen < $1.firstSeen }
                return $0.id < $1.id
            }
            for (idx, dev) in sorted.enumerated() {
                result[dev.id] = "Mac \(idx + 1)"
            }
        }

        return result
    }
}
