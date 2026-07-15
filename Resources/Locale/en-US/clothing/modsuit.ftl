# MODsuit module installation
modsuit-install-success = You slot {THE($module)} into {THE($suit)}.
modsuit-install-no-complexity = {CAPITALIZE(THE($suit))} doesn't have enough free complexity for {THE($module)}.
modsuit-remove-success = You pry {THE($module)} out of {THE($suit)}.

# MODsuit verbs
modsuit-verb-remove-module = Remove {$module}

# MODsuit examine
modsuit-examine-complexity = It is using [color=cyan]{$used}[/color] of [color=cyan]{$max}[/color] complexity.
modsuit-examine-modules-header = It has the following modules installed:
modsuit-examine-module-entry = - {$module}

# MODsuit deploy / seal
modsuit-sealed = {CAPITALIZE(THE($suit))} deploys and seals around you.
modsuit-unsealed = {CAPITALIZE(THE($suit))} retracts its parts.
modsuit-deploy-slot-blocked = Your {$slot} slot is occupied - {THE($suit)} can't deploy there.

# MODsuit seal verbs (right-click) and radial menu
modsuit-verb-seal-all = Seal all
modsuit-verb-unseal-all = Unseal all
modsuit-radial-toggle-part = Toggle {$part}
modsuit-radial-toggle-all = Seal / unseal everything

# MOD interface panel
modsuit-panel-title = MOD Interface Panel
modsuit-panel-section-status = Suit status
modsuit-panel-section-modules = Modules
modsuit-panel-section-hardware = Hardware
modsuit-panel-status = Status: {$status}
modsuit-panel-status-active = [color=lime]Active[/color]
modsuit-panel-status-deployed = [color=yellow]Deployed (inactive)[/color]
modsuit-panel-status-stowed = [color=gray]Stowed[/color]
modsuit-panel-complexity = Complexity: {$used} / {$max}
modsuit-panel-cell = Power cell: {$cap} kJ capacity
modsuit-panel-no-cell = Power cell: [color=red]none installed[/color]
modsuit-panel-no-modules = No modules installed.
modsuit-panel-module-entry = {$name} - {$state} (complexity {$complexity})
modsuit-panel-module-on = [color=lime]on[/color]
modsuit-panel-module-off = [color=gray]off[/color]
modsuit-panel-module-passive = passive
modsuit-panel-part-entry = {$name}: {$state}
modsuit-panel-part-deployed = [color=lime]deployed[/color]
modsuit-panel-part-stowed = [color=gray]stowed[/color]

# MODsuit modules (radial toggle)
modsuit-module-unpowered = {CAPITALIZE(THE($suit))} needs to be powered on to use modules.
modsuit-radial-module-on = {$module} (on)
modsuit-radial-module-off = {$module} (off)

# MODsuit power (activate)
modsuit-activated = {CAPITALIZE(THE($suit))} hums to life and seals shut.
modsuit-activated-partial = {CAPITALIZE(THE($suit))} powers on, but it isn't sealed against space - deploy every part first.
modsuit-deactivated = {CAPITALIZE(THE($suit))} powers down.
modsuit-power-empty = {CAPITALIZE(THE($suit))} runs out of power and shuts down.
modsuit-activate-not-deployed = You need to deploy every part before powering {THE($suit)} on.
modsuit-cant-remove-deployed = You can't take {THE($suit)} off while it's deployed - retract it first.
