namespace ProctoringGuard.Models;

public static class DeviceClassifier
{
    // Windows Bluetooth stack / adapter entries that are not real nearby devices
    private static readonly string[] InternalKeywords =
    {
        "enumerator", "rfcomm", "protocol", "personal area network", "radio",
        "adapter", "transport", "wireless bluetooth", "generic bluetooth",
        "realtek bluetooth", "qualcomm", "mediatek", "broadcom", "intel(r)",
        "microsoft bluetooth", "bluetooth device (", "gatt", "service discovery"
    };

    public static bool IsInternal(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Has(name, InternalKeywords);
    }

    public static DeviceCategory Classify(string name, string extra)
    {
        name  ??= "";
        extra ??= "";

        if (IsInternal(name)) return DeviceCategory.InternalSystem;

        // ── Wi-Fi access points ─────────────────────────────────────────────
        if (extra.Contains("access-point", StringComparison.OrdinalIgnoreCase))
        {
            if (Has(name, HotspotKeywords))  return DeviceCategory.Hotspot;
            if (Has(name, IspKeywords))      return DeviceCategory.ISPRouter;
            if (Has(name, OfficeKeywords))   return DeviceCategory.OfficeRouter;
            return DeviceCategory.Unknown;
        }

        // ── Bluetooth: class-of-device hint first ───────────────────────────
        if (extra.Contains("type:Phone"))        return DeviceCategory.Phone;
        if (extra.Contains("type:AudioVideo"))   return DeviceCategory.Headphones;
        if (extra.Contains("type:Peripheral"))   return DeviceCategory.Peripheral;
        if (extra.Contains("type:Wearable"))     return DeviceCategory.Wearable;
        if (extra.Contains("type:Computer"))     return DeviceCategory.Computer;
        if (extra.Contains("audiosink"))         return DeviceCategory.Headphones;

        // ── Name-based fallback ─────────────────────────────────────────────
        if (Has(name, "airpods", "buds", "earbud", "headset", "headphone", "earphone", "wh-1000", "jabra", "neckband"))
            return DeviceCategory.Headphones;
        if (Has(name, "watch", "band", "fitbit", "amazfit", "mi smart"))
            return DeviceCategory.Wearable;
        if (Has(name, "iphone", "galaxy", "pixel", "redmi", "oneplus", "oppo", "vivo", "realme", "phone"))
            return DeviceCategory.Phone;
        if (Has(name, "keyboard", "mouse", "trackpad"))
            return DeviceCategory.Peripheral;
        if (Has(name, "laptop", "macbook", "desktop"))
            return DeviceCategory.Computer;

        return DeviceCategory.Unknown;
    }

    public static string GetHotspotBrand(string ssid)
    {
        ssid ??= "";
        if (Has(ssid, "iphone"))                      return "Apple";
        if (Has(ssid, "galaxy", "sm-"))               return "Samsung";
        if (Has(ssid, "redmi", "xiaomi", "poco"))     return "Xiaomi";
        if (Has(ssid, "oneplus"))                     return "OnePlus";
        if (Has(ssid, "pixel"))                       return "Google";
        if (Has(ssid, "oppo"))                        return "Oppo";
        if (Has(ssid, "vivo"))                        return "Vivo";
        if (Has(ssid, "realme"))                      return "Realme";
        if (Has(ssid, "moto"))                        return "Motorola";
        return "Generic";
    }

    private static readonly string[] HotspotKeywords =
    {
        "iphone", "galaxy", "androidap", "redmi", "xiaomi", "poco", "oneplus",
        "pixel", "oppo", "vivo", "realme", "moto", "hotspot", "sm-"
    };

    private static readonly string[] IspKeywords =
    {
        "jio", "airtel", "bsnl", "act_", "act-", "hathway", "excitel", "tp-link",
        "tplink", "d-link", "dlink", "tenda", "netgear", "asus", "huawei", "zte"
    };

    private static readonly string[] OfficeKeywords =
    {
        "guest", "corp", "office", "staff", "college", "univ", "campus", "school", "lab", "eduroam"
    };

    private static bool Has(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
