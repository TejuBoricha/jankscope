# Rollback & Recovery Architecture (Stage 4)

> [!IMPORTANT]
> **Stage 4 Feature Specification.** This document specifies the rollback guarantees, atomic journal schema, and crash recovery mechanisms required for all system optimizations (spec §11.4).

---

## 1. The Rollback Contract

Every optimization action that touches a system setting must implement the **Complete Reversibility Contract**:
1. **Record Before Touch:** The exact existing state (type, binary data, string values, access permissions) must be flushed to the durable rollback journal *before* any modification occurs.
2. **Deterministic Rollback:** An action's rollback routine must restore the exact previous state, leaving zero residual configuration drift.
3. **Automated Boot Recovery:** If a crash or unexpected power failure occurs while an optimization is active, Jankscope detects the dirty state on the subsequent startup and automatically rolls back all changes safely.
4. **Fault-Tolerant Rollback:** If a rollback fails partially, the system records the failure, logs the exact registry or file path involved, and alerts the user with manual remediation instructions.

---

## 2. Durable Journal Schema

The rollback journal is stored in the local application data directory (`%LOCALAPPDATA%\Jankscope\journal.json`). Writes to the journal use write-ahead atomic file swaps with flush-to-disk guarantees.

```json
{
  "journalVersion": 1,
  "sessionId": "4a7f0e31-89cb-4d51-9bf6-2e80c45165d2",
  "startedUtc": "2026-11-04T12:00:00Z",
  "status": "Active",
  "actions": [
    {
      "actionId": "opt.game_bar.background_recording",
      "appliedUtc": "2026-11-04T12:01:15Z",
      "targetType": "RegistryDword",
      "targetPath": "HKCU\\System\\GameConfigStore\\GameDVR_Enabled",
      "originalValue": 1,
      "appliedValue": 0,
      "rollbackRecipe": "SetRegistryDword"
    }
  ]
}
```

---

## 3. Rollback Failure & Fault-Injection Testing

To ensure reliability, the rollback subsystem is subjected to rigorous fault-injection tests before Stage 4 deployment:
- **Crash Simulation:** Process termination immediately after writing the journal, midway through the registry write, and prior to the completion flag.
- **Permission Drop Simulation:** Simulating a scenario where access to a modified registry key is revoked prior to rollback.
- **Concurrent Modification:** Simulating external system updates (such as Windows Update or third-party tweakers) modifying the same setting while Jankscope has an active journal entry.
