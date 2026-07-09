disarm-action-disarmable = {CAPITALIZE(THE($targetName))} is not disarmable!
disarm-action-popup-message-other-clients = {CAPITALIZE(THE($performerName))} disarmed {THE($targetName)}!
disarm-action-popup-message-cursor = Disarmed {THE($targetName)}!
disarm-action-shove-popup-message-other-clients = {CAPITALIZE(THE($performerName))} shoves {THE($targetName)}!
disarm-action-shove-popup-message-cursor = You shove {THE($targetName)}!

# ── SS13-style shove/disarm system (DisarmingSystem) ──
# Args passed from C#: $user = shover, $target = shoved mob, $weapon = item used, $item = dropped item.
# THE()/CAPITALIZE() need the entity; OBJECT()/POSS-ADJ() give gendered him-her-them / his-her-their.

# General shove (get_shoving_message) - "-user" shown to the shover, "-others" to everyone else.
disarm-shove-user = You shove {THE($target)}!
disarm-shove-user-weapon = You shove {THE($target)} with {THE($weapon)}!
disarm-shove-others = {CAPITALIZE(THE($user))} shoves {THE($target)}!
disarm-shove-others-weapon = {CAPITALIZE(THE($user))} shoves {THE($target)} with {THE($weapon)}!

# Shoved into something solid, knocking them down
disarm-knockdown-user = You shove {THE($target)}, knocking {OBJECT($target)} down!
disarm-knockdown-others = {CAPITALIZE(THE($user))} shoves {THE($target)}, knocking {OBJECT($target)} down!

# Kicked onto their side (stagger finisher)
disarm-kick-user = You kick {THE($target)} onto {POSS-ADJ($target)} side!
disarm-kick-others = {CAPITALIZE(THE($user))} kicks {THE($target)} onto {POSS-ADJ($target)} side!

# Item knocked out of hand - "-target" shown to the person who dropped it.
disarm-drop-target = You drop {THE($item)}!
disarm-drop-others = {CAPITALIZE(THE($target))} drops {THE($item)}!

# Shoved onto a table (or other climbable)
disarm-table-user = You shove {THE($target)} onto the table!
disarm-table-others = {CAPITALIZE(THE($user))} shoves {THE($target)} onto the table!
