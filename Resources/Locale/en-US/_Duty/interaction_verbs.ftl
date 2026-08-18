# Ported from Goob-Station/Einstein Engines (see Content.Shared/_Duty/InteractionVerbs).

## Common interaction-verb system messages ("Interact" tab)

interaction-verb-invalid = Some requirements for this verb are not met. You cannot use it right now.
interaction-verb-cooldown = This verb is on cooldown. Wait {TOSTRING($seconds, "F1")} seconds.
interaction-verb-invalid-target = You cannot use this verb on that target.
interaction-verb-no-hands = You have no usable hands.
interaction-verb-cannot-reach = You cannot reach there.
interaction-verb-wrap-message = [italic]{$message}[/italic]

interaction-LookAt-name = Stare
interaction-LookAt-description = Stare into the void and see it stare back.
interaction-LookAt-success-self-popup = You stare at {THE($target)}.
interaction-LookAt-success-target-popup = You feel {THE($user)} staring at you...
interaction-LookAt-success-others-popup = {THE($user)} stares at {THE($target)}.

interaction-Hug-name = Hug
interaction-Hug-description = A hug a day keeps the psychological horrors beyond your comprehension away.
interaction-Hug-success-self-popup = You hug {THE($target)}.
interaction-Hug-success-target-popup = {THE($user)} hugs you.
interaction-Hug-success-others-popup = {THE($user)} hugs {THE($target)}.

interaction-KnockOn-name = Knock
interaction-KnockOn-description = Knock on the target to attract attention.
interaction-KnockOn-success-self-popup = You knock on {THE($target)}.
interaction-KnockOn-success-target-popup = {THE($user)} knocks on you.
interaction-KnockOn-success-others-popup = {THE($user)} knocks on {THE($target)}.

# The below includes conditionals for if the user is holding an item
interaction-WaveAt-name = Wave at
interaction-WaveAt-description = Wave at the target. If you are holding an item, you will wave it.
interaction-WaveAt-success-self-popup = You wave {$hasUsed ->
    [false] at {THE($target)}.
    *[true] your {$used} at {THE($target)}.
}
interaction-WaveAt-success-target-popup = {THE($user)} waves {$hasUsed ->
    [false] at you.
    *[true] {POSS-PRONOUN($user)} {$used} at you.
}
interaction-WaveAt-success-others-popup = {THE($user)} waves {$hasUsed ->
    [false] at {THE($target)}.
    *[true] {POSS-PRONOUN($user)} {$used} at {THE($target)}.
}

# Requires the target to click the same verb back within 8 seconds — see MutualConsentAction.
interaction-Handshake-name = Handshake
interaction-Handshake-description = Offer a handshake. The target has to shake back within 8 seconds.
interaction-Handshake-success-self-popup = You shake hands with {THE($target)}.
interaction-Handshake-success-target-popup = {THE($user)} shakes your hand back.
interaction-Handshake-success-others-popup = {THE($user)} and {THE($target)} shake hands.
interaction-Handshake-fail-self-popup = You offer your hand to {THE($target)} for a handshake. Wait for a response.
interaction-Handshake-fail-target-popup = {THE($user)} offers you a handshake. Right-click them and pick "Handshake" to shake back.
interaction-Handshake-fail-others-popup = {THE($user)} offers a handshake to {THE($target)}.

# Same mutual-consent mechanic as Handshake.
interaction-HighFive-name = High five
interaction-HighFive-description = Offer a high five. The target has to high-five back within 8 seconds.
interaction-HighFive-success-self-popup = You high-five {THE($target)}.
interaction-HighFive-success-target-popup = {THE($user)} high-fives you back.
interaction-HighFive-success-others-popup = {THE($user)} and {THE($target)} high-five each other.
interaction-HighFive-fail-self-popup = You raise your hand for a high five with {THE($target)}. Wait for a response.
interaction-HighFive-fail-target-popup = {THE($user)} raises a hand for a high five. Right-click them and pick "High five" to answer.
interaction-HighFive-fail-others-popup = {THE($user)} raises a hand for a high five with {THE($target)}.

interaction-Pat-name = Pat on the head
interaction-Pat-description = Give the target a friendly pat on the head.
interaction-Pat-success-self-popup = You pat {THE($target)} on the head.
interaction-Pat-success-target-popup = {THE($user)} pats you on the head.
interaction-Pat-success-others-popup = {THE($user)} pats {THE($target)} on the head.
interaction-Pat-delayed-self-popup = You reach out to pat {THE($target)} on the head...
interaction-Pat-delayed-target-popup = {THE($user)} reaches out to pat you on the head...
interaction-Pat-delayed-others-popup = {THE($user)} reaches out to pat {THE($target)} on the head...
interaction-Pat-fail-self-popup = You fail to pat {THE($target)} on the head.
interaction-Pat-fail-target-popup = {THE($user)} tries to pat you on the head, but fails.

interaction-Spit-name = Spit in face
interaction-Spit-description = Spit in the target's face. A blatant insult.
interaction-Spit-success-self-popup = You spit in {THE($target)}'s face.
interaction-Spit-success-target-popup = {THE($user)} spits in your face.
interaction-Spit-success-others-popup = {THE($user)} spits in {THE($target)}'s face.
interaction-Spit-delayed-self-popup = You work up some spit, aiming at {THE($target)}...
interaction-Spit-delayed-target-popup = {THE($user)} works up some spit, aiming at you...
interaction-Spit-delayed-others-popup = {THE($user)} works up some spit, aiming at {THE($target)}...
interaction-Spit-fail-self-popup = You fail to spit at {THE($target)}.
interaction-Spit-fail-target-popup = {THE($user)} tries to spit at you, but fails.

# No damage — pure RP effect.
interaction-Slap-name = Slap
interaction-Slap-description = Slap the target across the face. No damage — purely for effect.
interaction-Slap-success-self-popup = You slap {THE($target)} across the face.
interaction-Slap-success-target-popup = Ow! That hurts!
interaction-Slap-success-others-popup = {THE($user)} slaps {THE($target)} across the face.
interaction-Slap-delayed-self-popup = You raise your hand to slap {THE($target)}...
interaction-Slap-delayed-target-popup = {THE($user)} raises a hand to slap you...
interaction-Slap-delayed-others-popup = {THE($user)} raises a hand to slap {THE($target)}...
interaction-Slap-fail-self-popup = You fail to slap {THE($target)}.
interaction-Slap-fail-target-popup = {THE($user)} tries to slap you, but fails.

# Target is not a mob (counter/table/wall/window) — see InteractionVerbsComponent on TableBase/BaseWall/Window.
interaction-LeanOn-name = Lean on
interaction-LeanOn-description = Lean on the target — a counter, table, wall, or window.
interaction-LeanOn-success-self-popup = You lean on {THE($target)}.
interaction-LeanOn-success-target-popup = Someone leans on you.
interaction-LeanOn-success-others-popup = {THE($user)} leans on {THE($target)}.
interaction-LeanOn-delayed-self-popup = You start leaning on {THE($target)}...
interaction-LeanOn-delayed-target-popup = Someone starts leaning on you...
interaction-LeanOn-delayed-others-popup = {THE($user)} starts leaning on {THE($target)}...
interaction-LeanOn-fail-self-popup = You fail to lean on {THE($target)}.
interaction-LeanOn-fail-target-popup = Someone tries to lean on you, but fails.

# Instant, ranged, no fail popup (same shape as LookAt).
interaction-StareAt-name = Ogle
interaction-StareAt-description = Stare at the target intently — rude, but sometimes it says enough.
interaction-StareAt-success-self-popup = You ogle {THE($target)}.
interaction-StareAt-success-target-popup = You feel {THE($user)}'s intent, unbroken gaze on you.
interaction-StareAt-success-others-popup = {THE($user)} ogles {THE($target)}.

# Highlights the target for 8 seconds, fading in darkness; no fail popup (same shape as LookAt).
interaction-PointAt-name = Point at
interaction-PointAt-description = Point at the target, highlighting them for everyone nearby for 8 seconds. The highlight fades in darkness.
interaction-PointAt-success-self-popup = You point at {THE($target)}.
interaction-PointAt-success-target-popup = {THE($user)} points at you.
interaction-PointAt-success-others-popup = {THE($user)} points at {THE($target)}.
interaction-PointAt-delayed-self-popup = You raise a hand, pointing at {THE($target)}...
interaction-PointAt-delayed-target-popup = {THE($user)} raises a hand, pointing at you...
interaction-PointAt-delayed-others-popup = {THE($user)} raises a hand, pointing at {THE($target)}...
