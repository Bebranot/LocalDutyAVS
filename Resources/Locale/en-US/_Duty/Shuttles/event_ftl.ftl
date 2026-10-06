cmd-dutyftlcentcomm-desc = Sends a shuttle via FTL to a random free spot near CentComm (no coordinates disk needed).
cmd-dutyftlcentcomm-help = Usage: { $command } [hyperspace_seconds|-] [grid_or_entity_on_it]
    Without the second argument the grid you stand on is used. "-" means the default time.
cmd-dutyftlstation-desc = Sends a shuttle via FTL to a random free spot near the station.
cmd-dutyftlstation-help = Usage: { $command } [hyperspace_seconds|-] [grid_or_entity_on_it]
    Without the second argument the grid you stand on is used. "-" means the default time.

cmd-dutyftl-hint-time = [seconds in hyperspace, { $min }–{ $max }, or -]
cmd-dutyftl-hint-grid = [grid or entity on it]
cmd-dutyftl-bad-time = "{ $value }" is not a number of seconds.
cmd-dutyftl-time-clamped = Hyperspace time adjusted to { $value } s.
cmd-dutyftl-no-attached = You are not attached to an entity — pass the grid as the second argument.
cmd-dutyftl-not-on-grid = The entity is not on a grid — specify a shuttle.
cmd-dutyftl-not-grid = That is not a shuttle grid.
cmd-dutyftl-not-shuttle = { $grid } is not a shuttle.
cmd-dutyftl-already-ftl = { $grid } is already in FTL.
cmd-dutyftl-is-station = { $grid } is a station or CentComm and cannot be sent.
cmd-dutyftl-no-centcomm = CentComm is not loaded this round.
cmd-dutyftl-no-station = No station found to jump to.
cmd-dutyftl-no-spot = No free space for the shuttle near { $anchor }.
cmd-dutyftl-start-failed = The jump failed to start — see the server log.
cmd-dutyftl-override = Warning: normal FTL is blocked for this shuttle ({ $reason }) — the command overrides it.
cmd-dutyftl-started = { $grid } is jumping to { $anchor }: spot ({ $x }, { $y }), { $distance } m from its centre, { $time } s in hyperspace.
