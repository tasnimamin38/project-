extends Node

signal wallet_changed(wallet: Dictionary)
signal purchase_blocked(reason: String)

# Local development grants are intentionally separate from real purchases.
const SANDBOX_MODE: bool = true

var wallet: Dictionary = {
    "gold": 0,
    "diamond": 0,
    "platinum": 0
}

func grant_sandbox_currency(currency: String, amount: int) -> bool:
    if not SANDBOX_MODE:
        return false
    if not wallet.has(currency) or amount <= 0:
        return false
    wallet[currency] = int(wallet[currency]) + amount
    wallet_changed.emit(wallet.duplicate(true))
    return true

func request_purchase(_sku: String) -> Dictionary:
    var reason := "REAL_PURCHASES_DISABLED: trusted backend and payment verification are not connected"
    purchase_blocked.emit(reason)
    return {"ok": false, "reason": reason}
