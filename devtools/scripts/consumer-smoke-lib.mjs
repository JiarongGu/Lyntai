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

/**
 * Entry names inside a zip archive (a `.snupkg` is one), read from its CENTRAL DIRECTORY.
 *
 * This replaced `tar -tf`, which was never able to do the job on this machine and failed in the permissive
 * direction for the whole life of the check. The call site read `if (listing.status === 0 && !hasPdb)` — so
 * a tar that could not open the archive skipped the check and the step printed its ✓ regardless. Measured
 * 2026-08-15: GNU tar 1.35 is what is installed here, GNU tar cannot read zip at all (`tar -tf` on a zip
 * exits 2, "This does not look like a tar archive"), and the comment beside the call asserted the opposite —
 * it assumed bsdtar. So the release gate's symbol-package check had never once run here.
 *
 * Reading the archive directly removes the dependency rather than swapping it for another guess about which
 * tar is installed. The central directory is the authority on what an archive contains: scanning for local
 * file headers instead would also match a name inside compressed DATA, and would miss nothing only by luck.
 */
export function zipEntryNames(buffer) {
  const names = [];
  if (!buffer || buffer.length < 22) return names;

  // Locate the End of Central Directory record, which is last and variable-length (it may carry a comment),
  // so it is found by scanning BACKWARDS for its signature rather than assumed to be at a fixed offset.
  const EOCD = 0x06054b50;
  let eocd = -1;
  for (let i = buffer.length - 22; i >= 0; i--) {
    if (buffer.readUInt32LE(i) === EOCD) { eocd = i; break; }
  }
  if (eocd < 0) return names;

  const count = buffer.readUInt16LE(eocd + 10);
  let at = buffer.readUInt32LE(eocd + 16);

  for (let i = 0; i < count && at + 46 <= buffer.length; i++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) break;   // not a central-directory header: stop rather than guess
    const nameLen = buffer.readUInt16LE(at + 28);
    const extraLen = buffer.readUInt16LE(at + 30);
    const commentLen = buffer.readUInt16LE(at + 32);
    names.push(buffer.toString('utf8', at + 46, at + 46 + nameLen));
    at += 46 + nameLen + extraLen + commentLen;
  }
  return names;
}
