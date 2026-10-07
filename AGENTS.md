# Hard engineering rule: native .NET 10 foundations

This invariant applies to every implementation, review and handoff in this MVC port project.

- **Protect the public MVC contract.** Only explicitly approved native differences may change that boundary; `System.Data.Linq.Binary` models migrating to native `byte[]` are an approved difference.
- Use native .NET 10 fundamentals first and replace internal implementations freely to serve that public contract. Legacy source identity and internal behavior are not preservation requirements; source comparisons provide provenance and change evidence.
- Add compatibility code only for a demonstrated application-contract need. Reuse native hosting, Tasks, routing, binding/input, identity, protection, state and build services where appropriate.
- Never make obsolete legacy hosting, types, platforms or historical Framework/IIS/binary/wire parity automatic completion requirements.
- A proposed departure requires an explicit user decision **before implementation**. Do not infer approval from an absent-type count or an old tracker entry.
- Reviewers must reject unjustified legacy reimplementation or scope expansion.
- Include this invariant and the current agreed native contract in every implementation/review handoff to another agent.

Current decision: use native `byte[]` models and existing `ByteArrayModelBinder`; document migration from `System.Data.Linq.Binary`. Do not port LINQ-to-SQL or require `LinqBinaryModelBinder` for completion. Existing helper compatibility does not justify expanding the historical platform.

This is project engineering guidance, not a tool permission or approval policy. Historical evidence remains preserved; current acceptance follows the user's explicit native .NET 10 scope.
