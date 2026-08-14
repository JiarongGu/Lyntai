// The pure steps of `consumer-smoke`, extracted so they can be tested without paying for the minutes.
//
// consumer-smoke resists the usual treatment: the thing worth testing IS the pack → restore → build → run,
// and a seam that stubbed the pack would test the bookkeeping and none of the risk. That reasoning is right
// about the PROCESS and wrong about these two steps, which are pure string work sitting inside it — and
// each fails SILENTLY when wrong, turning a real check into a green run that verified nothing. Those are
// exactly the ones worth pinning; the rest is honestly left to the live process.

/// Package ids from a feed listing, lowercased the way NuGet's global-packages cache spells directories.
///
/// Trims the exact `.{version}.nupkg` SUFFIX rather than a fixed LENGTH. A file ending in `.nupkg` but not
/// in this version — a leftover from an earlier run, a hand-dropped package — would otherwise have the
/// wrong number of characters cut off and yield a garbage id. A garbage id evicts nothing, the stale cached
/// copy survives, and the smoke silently restores and tests the OLD package while reporting success. That
/// is not hypothetical: it is the exact failure the eviction step was added to fix, caught when a
/// newly-added public method was "missing" from a package that demonstrably contained it.
///
/// `.snupkg` files do not match, and not by accident of filtering: `"x.snupkg".endsWith(".nupkg")` is false
/// because the character before `nupkg` is `s`, not `.`.
export function packageIdsFrom(feedFiles, version) {
  const suffix = `.${version}.nupkg`;
  return (feedFiles ?? [])
    .filter((f) => f.endsWith(suffix))
    .map((f) => f.slice(0, f.length - suffix.length).toLowerCase());
}

/// Whether a symbol package's archive listing carries a PDB.
///
/// An empty `.snupkg` is pushed automatically beside its `.nupkg` and offered to nuget.org's symbol
/// validation for nothing — a real defect this guard found on the new bundle before 2.0.1, where a package
/// shipping no assembly produced a symbol package with no PDB.
///
/// Matches a `.pdb` ENTRY rather than the substring anywhere in the listing. A listing mentioning `pdb` in
/// a directory name or a source file called `pdb.cs` would otherwise pass a package that carries no symbols
/// at all — a false PASS, which is the direction that matters for a guard.
export function listingHasPdb(listing) {
  return (listing ?? '').split('\n').some((line) => line.trim().toLowerCase().endsWith('.pdb'));
}
