// SPDX-FileCopyrightText: 2025 LocalDuty
// SPDX-License-Identifier: MIT

using Robust.Shared.Audio.Systems;

namespace Content.Shared._Duty.PocketPlayer;

public abstract class SharedPocketPlayerSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
}
