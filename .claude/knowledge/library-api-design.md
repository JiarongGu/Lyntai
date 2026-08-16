---
name: library-api-design
applies_when: designing or changing any public API, or a consumer asks for a feature
enforces: generalize the request, never ship its shape; no consumer vocabulary in the library; seams over flags; every public type earns its keep
---

# Library API design — ship the general capability, not the caller's shape

**When a consumer asks for a feature, the library ships the reusable capability underneath it — never the
consumer's specific shape.**

## Why

A library grows one feature request at a time, and each request arrives wearing the caller's vocabulary,
the caller's defaults, and the caller's edge case. Shipping that shape is faster once and wrong forever:
the next consumer needs the same capability with different specifics and now has to either bend to a
stranger's model or ask for a second, near-duplicate API. Two near-duplicate APIs is how a library
becomes unmaintainable, and neither can be removed because both are public.

The discipline is not "say no". It is to find the general thing the request is an instance of, and ship
that — usually a seam the consumer fills in, rather than behaviour the consumer selects.

## How to apply

- **Translate the request before implementing it.** "Our app needs X to do Y" becomes: what is the
  capability, and what part of it is the caller's policy? Ship the capability; let the caller supply the
  policy.
- **No consumer vocabulary in the library.** If a type, member, or option is named after one consumer's
  domain, the abstraction has not been found yet. The library's names should read sensibly to someone who
  has never seen that consumer.
- **Prefer a seam to a flag.** A boolean that selects between two behaviours is usually two consumers
  disagreeing; an interface they each implement resolves it permanently and costs the library nothing.
  Flags multiply: two become four combinations, and only some of them are ever tested.
- **Prefer an options record to magic values.** Anything a caller might reasonably want different — a
  timeout, a size limit, a path, a retry count — is a property with a documented default, not a constant.
- **Every public type earns its keep.** Public surface is a permanent promise under semantic versioning.
  If a type exists only because it was convenient during implementation, make it internal before the
  release, not after — after is a breaking change.
- **Say no to genuinely app-specific requests, and say what to do instead.** Usually the answer is a seam
  the consumer implements on their side. Record the refusal and the reason, or it will be re-litigated.
