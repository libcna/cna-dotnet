# binding-generator (deferred maintenance tooling)

This directory records a tooling idea, not an unfinished XNA compatibility feature. The strict
`CNA.XnaCompat` facade reached exact metadata for the selected XNA 4.0 profiles without a generator,
and the broad compatibility campaign is now bounded. No generator is planned merely to replace
working hand-written code.

Per `libcna/cna`'s `docs/binding.md` (*What to generate and what to design*), automation is a
good fit for:

- raw FFI declarations,
- enum mappings (see the two parallel `Keys` enums this would remove),
- simple value structs with implicit-conversion pairs (see the duplicated
  `Vector2`/`Color` in `CNA.Framework` vs `CNA.XnaCompat`),
- repetitive resource wrapper boilerplate,
- documentation/parity tables.

and a poor fit for anything involving actual API/behavior design (the `Game`
callback bridge, `ContentManager.Load<T>` dispatch, ownership rules) — those
stay hand-written.

## Maintenance disposition

Classification: **obsolete as a campaign task; possible future tooling**. Reconsider it only when a
specific repetitive maintenance change has enough examples, tests, and stable inputs to make
generation safer than editing the existing facade. Do not grow `CNA.Framework` or
`CNA.XnaCompat` merely to create work for this tool.
