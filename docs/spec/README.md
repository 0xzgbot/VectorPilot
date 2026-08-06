# Spec pack (M0 — VP-1000)

Landing zone for the port's shared artifacts, emitted from the ShopPilot Mac repo:

- `schema.md` — `.shoppilot` document JSON schema (mirror Swift Codable keys exactly)
- `presets.json` — 72 stock sheet presets
- `tool_db_seed.json` — 13 tool classes / 17 defaults (3-part linkage)
- `golden/` — hand-derived golden G-code files (byte-for-byte parity gate)
- `verify_manifest.md` — the 97 verify CLTs with PASS lines

Populated by VP-1000. Do not hand-edit goldens.
