# Repeat market analysis

Previously, each analysis received a fresh market snapshot without its previous
recommendation. A second run could propose another NEXT trigger even while
the first condition was still valid. A two-run synthetic regression reproduced
the missing context after a new closed candle crossed the earlier level.

Market snapshots now include `previousSignal`: the latest saved recommendation
for the exact contract, its creation time and age, and its invalidation. Context
is omitted after three hours, for future-dated results, different history
sources, or a changed manual position (including quantity or average entry).
Previous snapshots are not nested, and journal prompts receive no market signal.

Market default prompt v8 requires the model to evaluate the original condition
and expiry first. A valid pending condition keeps its levels and original clock.
A confirmed condition can become a NOW call if an entry remains available;
expired, invalidated or missed entries require a specific explanation before a
replacement. A quote touching a level is distinct from candle confirmation.
The prior suggestion never implies an executed trade. Custom prompt overrides
remain unchanged; users can adopt the updated default in Settings → Prompts.

Existing backend freshness, position and level validation remains authoritative.
App-overridden WAIT results carry `decision.guardCode` and a visible app-check
label. Home Assistant logs record the final action, model/app-check origin and
safe guard category without prices, positions or prompt text.

Run `--signal-continuity-test` or the full backend `--self-test`, and
`node --test tests/*.test.mjs`. These verify the context contract, isolation,
prompt instructions, validation and display; they do not establish that a live
model will accept a particular setup or that a user-reported candle occurred.
