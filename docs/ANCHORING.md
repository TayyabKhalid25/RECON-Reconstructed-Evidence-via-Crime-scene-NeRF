# Anchoring

How the digital twin gets put back in the room it came from. This file closes handbook
Section 03 Decision 1, which adopted "printed marker plus ARCore Cloud Anchors" but left four
things marked `verify`. All four are checked below, against live sources, on **2026-09-02**.

Read `docs/FRAMES.md` first. It owns the coordinate convention and `unitScale`; this file owns
where the scene origin lands in the real world and how that survives a session ending.

## The short version

- **The marker aligns. The cloud anchor persists.** Two different jobs, and only the marker
  gives scale. Cloud Anchors never give scale, so the marker stays load bearing even after
  anchoring works.
- **One cloud anchor per scene, hosted at the marker origin.** Never one per evidence marker.
- **Keyless authorization is mandatory**, not a preference: an API key caps anchor lifetime at
  24 hours, which is shorter than the gap between two lab sessions.
- **Nothing about the web dashboard or remote viewing needs an anchor at all.**

## Verified facts

| Question | Answer | Source |
|---|---|---|
| Is Cloud Anchors available and maintained? | Yes. Docs actively updated 2026; `arcore-unity-extensions` v1.54.0 shipped 2026-04-22 | [Cloud Anchors overview](https://developers.google.com/ar/develop/cloud-anchors), [releases](https://github.com/google-ar/arcore-unity-extensions/releases) |
| Free? | No published price. Quota limited, and the docs never ask for billing to be enabled on the GCP project. `verify` in console when we enable it | [Cloud Anchors quickstart](https://developers.google.com/ar/develop/java/cloud-anchors/quickstart-android) |
| Persistence window | **1 to 365 days.** Default 1 day with an API key, 365 days with keyless. TTL over 1 day *requires* keyless | [Android developer guide](https://developers.google.com/ar/develop/java/cloud-anchors/developer-guide), [codelab](https://codelabs.developers.google.com/codelabs/arcore-cloud-anchors) |
| Quotas | 30 host requests/min and 300 resolve requests/min, per IP **and** project. 40 concurrent anchor operations per session. Anchors per project: unlimited | [Unity developer guide](https://developers.google.com/ar/develop/unity-arf/cloud-anchors/developer-guide) |
| Does it support our Unity 6.3 / AR Foundation 6.5? | Extensions declares AR Foundation 6 support since v1.48.0 via the `arf6` branch and tarballs. It was **not** built against AR Foundation 6.5 specifically, and it still compiles against deprecated ARF5 symbols. This is the one remaining risk, and FTW-50 is the spike that settles it | [ARF6 upgrade notes](https://developers.google.com/ar/develop/unity-arf/upgrade-to-ar-foundation-6) |
| Platforms | Android and iOS, all ARCore supported devices | [Cloud Anchors overview](https://developers.google.com/ar/develop/cloud-anchors) |
| Server side management | Real API: list, get, extend TTL, delete, at `https://arcore.googleapis.com/v1beta2/management/anchors`. OAuth2 service account, scope `arcore.management` | [Management API](https://developers.google.com/ar/develop/cloud-anchors/management-api) |

### The old endpoint, and why the version numbers matter

The original `arcorecloudanchor.googleapis.com` endpoint stopped being supported after
**31 August 2023** and now appears in the console as "ARCore Cloud Anchor API (Legacy)". The
live service is the **ARCore API** (`arcore.googleapis.com`). Enable the right one; the legacy
entry is still listed and still clickable. Apps need ARCore SDK 1.12.0 or newer to host or
resolve at all, which we comfortably clear.

Google's stated deprecation policy for Cloud Anchors: if they discontinue it they announce it
in the release notes and make commercially reasonable efforts to keep the service running for
**one year** after that announcement. That is the honest ceiling on this dependency, and it is
long enough to cover this project twice over.

### Azure Spatial Anchors, for the defence answer

**Azure Spatial Anchors was retired on 20 November 2024**, announced 1 December 2023. So the
proposal's re-architecture is a response to a checked fact, not a preference:

> The proposal originally depended on Azure Spatial Anchors. We found it had been retired in
> November 2024 and moved the persistence layer to ARCore Cloud Anchors, which also removed a
> platform dependency because our capture and AR client are already Android first.

Source: [Azure Spatial Anchors retirement](https://azure.microsoft.com/updates/azure-spatial-anchors-retirement),
[Microsoft Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/azure-spatial-anchors).

## Where anchors are needed, and where they are not

This is the question most of the anchoring literature skips, and getting it wrong costs weeks
of work on a feature nobody in the demo path uses.

| Context | Cloud anchor? | Why |
|---|---|---|
| Web dashboard splat viewer | **No** | The splat is a self contained coordinate system. There is no physical room to align to |
| Unity as a remote 3D or VR viewer | **No** | Same. Orbit camera, no AR session, no anchor. Physics still works, it is all virtual |
| On site AR, first session, marker still taped to the floor | **No** | The marker gives position, rotation *and* metric scale in one detection. A cloud anchor would add nothing and gives no scale |
| On site AR, marker removed, days later | **Yes** | This is the only way back to the same origin. A released crime scene does not keep our A4 target on the floor |
| Two investigators, same room, same time | **Yes** | The second device resolves the anchor the first one hosted. This is D12, 01 Nov |
| Evidence markers, POIs, trajectories, spatter | **No** | They are children of `SceneRoot` and live in scene coordinates in our database |
| Long AR session, tracking has drifted | Optional | Re-detecting the marker is cheaper and more accurate. A resolve also works if the marker is gone |

### One anchor per scene, hosted at the marker origin

The tempting mistake is to host a cloud anchor per evidence marker so each one "sticks" to the
real world. Do not. Reasons, in order of how much they cost to discover late:

1. **The twin has exactly one origin.** POIs are already positioned relative to it. Anchoring
   them individually introduces N independent re-localisation errors where there was one, and
   they will disagree with each other and with the splat.
2. **Custody.** A POI position is a row in our database, inside the hash chain, in scene
   coordinates we can verify. A cloud anchor id is an opaque handle to state on Google's
   servers that we cannot verify, cannot re-derive and cannot include in the chain.
3. **Quota and expiry.** One anchor per scene means one host call per scene, against a limit of
   30 per minute, and one TTL to keep track of. One per POI means the twin can partially
   expire, which is a genuinely horrible bug to diagnose on a phone.

So: host at the marker derived pose, in the same session that detected the marker, while
feature map quality reads Good. Then `anchor space == scene origin` and the transform we store
is identity.

### What the stored transform is for

`POST /api/scenes/:id/anchor` stores position, a unit quaternion and a uniform scale
(see FTW-47 and `docs/API.md`) rather than a 4x4 matrix. That transform is
**anchor space to scene space**, and it exists so a re-host at an arbitrary pose still works:
if the marker is gone and the anchor gets hosted at some convenient corner of the room instead,
the offset back to the scene origin is recoverable. In the normal path it is identity.

> `scale` on the anchor is the anchor to scene fit and is normally **1**. It is not
> `Scene.unitScale`, which converts scene units to metres and comes from the marker. Applying
> one where the other belongs is the double scale bug, and it looks like a mostly correct twin.

### The fallback chain

Implement all three, in this order, and show the user which one is active.

| Priority | Path | Metric? | Notes |
|---|---|---|---|
| 1 | Marker detected | Yes | Authoritative. Gives pose and scale together. Re-detect opportunistically to correct drift |
| 2 | Cloud anchor resolved, plus stored transform | Yes, but with resolve error on top | Needs network and a room that still looks like the room. Report the accuracy separately |
| 3 | Manual placement, user taps a plane | **No** | Flag the session as not metric verified in the UI and refuse to export measurements from it |

Path 3 is not a cop out, it is the risk register entry from handbook Section 17 ("anchoring
service unavailable") made real, and it is what keeps the demo alive on a hotel network. What
makes it safe is that it is visibly labelled and cannot produce an evidential measurement.

## Accuracy, and what we are allowed to claim

The proposal commits to **2 cm positional accuracy**. That number belongs to the **marker**
path and must be measured there. Google publishes no accuracy figure for Cloud Anchor
re-localisation, and the error compounds with distance from the hosted anchor.

So the honest structure, which is also the handbook's test matrix row for anchor drift:

- **Marker path**: measured against physical ground truth. This is where 2 cm is claimed or not.
- **Cloud anchor path**: measured as offset from the marker derived origin, same room, across
  sessions and across two devices. Reported as mean and max centimetres, in
  `docs/RESULTS.md`, with textured and bare room variants. Whatever it comes out as, it is a
  result, not a failure, and it answers the feature density research question.

Never quote the 2 cm figure for a scene that was placed by a cloud anchor resolve unless that
path has been measured to meet it. FTW-56 is that measurement.

## Authorization: keyless, and the keystore trap

Keyless is not optional for us. An API key caps anchor lifetime at 24 hours; our scenes need to
outlive a weekend.

**Android**
1. Enable the **ARCore API** on a GCP project.
2. Create an OAuth 2.0 client ID of type Android, with the app's package name and the SHA-1
   fingerprint of the signing keystore.
3. Add `com.google.android.gms:play-services-auth:16+` to the app Gradle dependencies via a
   custom main Gradle template.
4. If minifying, keep the ARCore auth and GMS classes in ProGuard.

> **The trap.** The SHA-1 is per keystore, and Unity's default debug keystore is different on
> every machine. Three developers building to three phones means three fingerprints, or
> `ErrorNotAuthorized` on two of them, which looks exactly like a broken anchor rather than a
> build configuration problem. Fix it once: generate **one shared custom keystore**, commit its
> SHA-1 to `docs/NETWORK.md`, keep the keystore itself out of git and pass it around like any
> other secret, and register that single fingerprint. Register the release fingerprint at the
> same time so demo day is not the first time a signed build talks to the API.

**iOS** needs a different thing entirely: a backend endpoint that mints RS256 JWTs signed with
a service account key, `aud` of `https://arcore.googleapis.com/`, one hour expiry, handed to
`ARAnchorManager.SetAuthToken()`. That is Track B work, not Unity work, and it is the reason
the Android or iOS scope question below has to be answered before FTW-57 gets picked up.

## Custody and privacy, the part a panel will ask about

Hosting a cloud anchor **uploads visual feature data derived from the crime scene to Google's
servers**. Not photographs, but a 3D feature map of a room that is, in the fiction of this
project, an active investigation site. For a project whose whole pitch is chain of custody,
that deserves to be stated rather than discovered.

What we do about it, all cheap, all defensible:

1. **Say it in the report** as a stated limitation of the anchoring approach, alongside the
   note that the alternative (marker plus a server side transform, path 1 plus our own
   database) keeps everything inside our custody boundary at the cost of cross device resolve.
2. **Audit host and resolve** as first class actions in the hash chained log. Anchoring asserts
   where in the physical world a piece of evidence sits, which is exactly the kind of claim the
   audit log exists to make accountable. FTW-47 already audits the store.
3. **Set TTL deliberately** per scene rather than defaulting to a year, and
4. **purge on case close** through the Management API `DELETE`, with an audit row for the
   deletion. That turns "we uploaded scene data to a third party" into "we uploaded scene data
   to a third party under a stated retention policy, and here is the log of it being deleted",
   which is a completely different sentence in a viva.

## Integration plan

Track C owns the device side, Track B owns the server side, and the two meet at the anchor
endpoints that already exist in PR #18. Nothing here needs the splat renderer to be finished,
so it can proceed in parallel with the mobile rendering work.

### Phase 0, settle the dependency (this week, before anything is built on it)

The spike, **FTW-50**, and it tests two paths in one sitting because the cheaper one might just
work:

- **Path A, no new dependency.** AR Foundation 6.5 has a built in persistent anchor API
  (`TrySaveAnchorAsync`, `TryLoadAnchorAsync`, `TryEraseAnchorAsync`) and the ARCore provider
  implements it *on top of Cloud Anchors* using our GCP project. We already have
  `com.unity.xr.arcore` 6.5.0, so this costs zero packages. The catch is that Unity documents
  the returned `SerializableGuid` as platform specific and not transferable, and AR Foundation's
  separate "shared anchors" feature lists ARCore as unsupported. So Path A is plausibly fine
  for **persistence** on one device and undocumented for the **two device** story. Test exactly
  that: save on phone 1, ship the GUID through our own API, load on phone 2.
- **Path B, the documented route.** `arcore-unity-extensions` v1.54.0, `arf6` tarball, which
  exposes `HostCloudAnchorAsync` and `ResolveCloudAnchorAsync` and hands us the **string Cloud
  Anchor id** that is explicitly meant to be shared between devices. Risk: it predates
  AR Foundation 6.5 and still uses deprecated ARF5 symbols, so it may not compile clean.

Decide by what actually round trips on two phones, write the answer into `docs/STACK.md` with
the exact version, and if Path B wins, pin the tarball rather than tracking the `arf6` branch.
If Path B cannot compile against AR Foundation 6.5, the fallback is to pin AR Foundation and
ARCore XR back to the version Extensions was verified against, and that is a decision for all
three of us because it touches the Unity project everyone builds.

### Phase 1, authorization (parallel with Phase 0)

**FTW-51.** GCP project, ARCore API enabled, one shared keystore, one OAuth client ID, Gradle
template. Done when a phone hosts an anchor with a 30 day TTL and no `ErrorNotAuthorized`.
This gates everything and has nothing to do with our code, so it should not wait for Phase 0.

### Phase 2, host (target I2, 20 Sep)

**FTW-52.** On the session that detected the marker:

```
ARTrackedImageManager detects marker (physicalSize 0.170)
  -> place SceneRoot at the image pose, scale by metadata.unitScale
  -> poll EstimateFeatureMapQualityForHosting each frame, show it in the UI
  -> when quality == Good, host one anchor at the SceneRoot pose, ttlDays from config
  -> POST /api/scenes/:id/anchor { anchorId, provider, position, rotation, scale: 1, ttlDays, deviceLabel }
```

The quality gate is the whole ticket. Hosting on `Insufficient` returns an anchor id that never
resolves again, and the failure surfaces days later on a different phone. Google's guidance:
keep the camera on the point of interest and *walk around it* at roughly constant distance
rather than pivoting in place, wait a few seconds after session start, and avoid blank walls
and reflective surfaces. Every one of those is also good capture practice for the splat, so
`docs/CAPTURE.md` and the host UX can say the same thing.

### Phase 3, resolve and the fallback chain (target D12, 01 Nov)

**FTW-53.** `GET /api/scenes/:id/anchor` on the second device, resolve, apply the stored
transform, render. Then the three path chain above with a visible indicator of which path is
live, and the not metric verified flag on path 3. Two phones seeing the twin in the same
physical place at the same time is the D12 deliverable.

### Phase 4, the server side follow ups

- **FTW-54**, small: accept `ttlDays` and compute `expiresAt` server side rather than trusting
  a client supplied timestamp. The phone knows the TTL it asked for; the server should own the
  clock.
- **FTW-55**, custody: service account, Management API purge on case close, audit row.
- **FTW-57**, blocked on scope: the iOS JWT endpoint. Only exists if iOS is in scope.

### Phase 5, measure it

**FTW-56.** Anchor drift, marker origin versus cloud resolved origin, across sessions and
across two devices, textured room and bare room. Into `docs/RESULTS.md` with date and device.
This is a test case in Chapter 7, and it is the number that keeps the 2 cm claim honest.

### Ticket index

| Ticket | Track | What |
|---|---|---|
| FTW-50 | Unity | Dependency spike, built in persistent anchors versus ARCore Extensions |
| FTW-51 | infra | GCP project, ARCore API, keyless auth, one shared keystore |
| FTW-52 | Unity | Host one anchor at the marker origin, behind the quality gate |
| FTW-53 | Unity | Resolve on a second device, plus the fallback chain |
| FTW-54 | Web | `ttlDays` in, `expiresAt` computed server side |
| FTW-55 | Web | Purge on case close via the Management API |
| FTW-56 | Unity | Anchor drift measurement |
| FTW-57 | Web | iOS JWT endpoint, blocked on the iOS scope decision |
| FTW-58 | proposal | Proposal anchoring text, verified facts and the custody limitation |

Existing tickets this touches: **FTW-47** (the anchor endpoints, PR #18) and **FTW-11**, which
closed with the four verifications still open; the checked facts are commented there.

## Open questions

| Question | Who decides | Blocks |
|---|---|---|
| Android only, or Android and iOS? | All three | FTW-57, and how much of the MacBook build day matters |
| TTL per scene: 30 days, or the full 365? | All three, custody question as much as a technical one | FTW-54 |
| If Extensions will not compile against AR Foundation 6.5, do we pin AR Foundation back? | All three, it touches everyone's Unity project | FTW-50 outcome |

## Sign off

- [ ] Tayyab has read the Phase 0 spike and agrees it is the right two paths to test
- [ ] All three know keyless auth needs one shared keystore, not three debug ones
- [ ] Android or iOS scope answered
- [ ] Tayyab initials: ____
- [ ] Wahaj initials: ____
- [ ] Faizan initials: ____
