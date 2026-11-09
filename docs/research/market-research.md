# cocoach market research: AI / live coaching for iRacing and sim racing

Task: `cocoach-market-research` (scout, market half). Report date: 2026-11-09. All product facts were pulled live on this date through DeepAPI (page scrapes, web search, Reddit, YouTube) unless a claim is marked otherwise. A sibling scout covers technology and architecture, so tech appears here only where it explains a competitor's capability.

Legend: **[V]** = read on the vendor's or primary source's own page today. **[S]** = secondary source (review site, forum, press) only. **[U]** = unverified or could not be confirmed. Every URL is the page the claim came from.

---

## 0. Executive summary

1. **The "AI coach for iRacing" idea is not a white space. It is a crowded, fast-moving category.** I found at least 20 products that coach iRacing drivers with AI, from funded startups to hobby projects. Roughly 12 coach live by voice. Several are free ([TakeP1](https://takep1.com/race-engineer/home), [TheTenths](https://www.thetenths.com/coaching), [Track Impulse](https://track-impulse.com/sim-racing-coach), [FlyLap](https://flylapsim.com/), [Simulator Controller via Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/)).
2. **No product I found coaches from the live screen. All of them read iRacing's telemetry (the SDK).** DeltaCoach states it connects through "the official SDK" at 20 Hz ([DeltaCoach](https://deltacoach.getgordonai.com/)). Apex says it turns "live iRacing... telemetry" into speech ([Apex](https://apexsimcoach.com/)). Screen or video analysis exists only at the edges: [Race.Ninja](https://www.race.ninja/help/ai-coaching/how-does-ai-coaching-work) (uploaded video plus telemetry), [Callup.gg](https://callup.gg/ai-coach/f1) (F1 clips) and hobby or hackathon repos ([PitLane AI](https://github.com/Adityaagulatii/cursor_hacker)). The captain's "sees your live screen" premise is therefore a differentiator **only if** it delivers something telemetry cannot. It should not be the foundation (see §4).
3. **The incumbent complaint is consistent: advice is shallow ("brake X m later"), the reference lap is wrong for the driver, overlays cost CPU, and a free stack (Garage 61 + Bloops, or a general LLM fed telemetry) is seen as good enough.** Drivers say a human coach beats all of it. Sources and quotes in §5.
4. **The gap that shows up repeatedly is context and continuity.** Users want a coach that knows their history, answers "why", and debriefs after the race. This is the "memory + post-race chatbot" the captain asked for. A handful of new entrants are starting here ([mental apex racing](https://mentalapexracing.com/), [RACEngineer.ai](https://racengineer.ai/), [Apex](https://apexsimcoach.com/), [TheTenths](https://www.thetenths.com/coaching)), but none is established. That is the most defensible wedge I found.
5. **Platform risk is real.** iRacing's own February 2026 development update says its collaboration with Microsoft Research "has reached the integration phase" and will "initially fuel new coaching and feedback tools" ([iRacing Feb 2026 update](https://www.iracing.com/iracing-development-update-february-2026/) [V]). Details such as "Large Action Model" and "real-time" come only from a fan site ([iRacerHub](https://iracerhub.com/iracing-microsoft-research-ai-driver-coaching/) [S]). iRacing also made Cosworth Pi Toolbox its official data-analysis partner ([iRacing/Cosworth](https://www.iracing.com/cosworth-and-iracing-unite-to-redefine-data-analysis-in-sim-racing/) [V]).
6. **Market size is modest and the price floor is $0.** iRacing says it has "over 350,000 sim racers" ([iracing.com](https://www.iracing.com/)). Track Titan, the best-funded rival, claims 200,000+ users across games and raised a $5M seed in December 2025 ([Partech](https://partechpartners.com/news/track-titan-raises-5-million-for-ai-powered-strava-for-motorsport)). Paid AI features cluster at about $4–$12 a month. Reality check in §6: a realistic iRacing-only outcome is low single-digit millions of dollars a year, not a venture-scale market, unless cocoach expands to other sims or other audiences.
7. **Recommendation (detail in §7):** build telemetry-first. The MVP is a lightweight live voice coach plus a post-race chat coach with persistent memory. Treat screen and vision as an optional enrichment. Avoid setups, AI that moves the wheel or pedals, and heavy overlays. Offer a real free tier and one paid tier at roughly $8 a month.

**Decisions that belong to the captain (not decided here):** whether to keep "sees the live screen" as the headline premise or reposition as telemetry-first with vision as an add-on; and whether the platform risk from iRacing's own coaching work is acceptable. Both are repeated in §7.

---

## 1. What I did and how (evidence trail)

All web research went through DeepAPI per `~/.claude/CLAUDE.md`. I read the `deepapi` skill and its `deep-research.md` and `scraping.md` references first. A throwaway helper (`dapi.py`, kept in the session scratchpad, not in the repo) added the required headers, an `Idempotency-Key` and polling. Raw JSON outputs also live only in the scratchpad, so the quoted excerpts below are the surviving evidence.

| Step | Endpoint | Count | Notes |
|---|---|---|---|
| Open-web discovery | `POST /v1/search/web` | 16 distinct queries (more than the 5 required) | e.g. "AI coach for iRacing live voice coaching", "Track Titan iRacing AI coach pricing", "Garage 61 iRacing pricing features", "AI sim racing coach screen capture vision model...", "iRacing EULA third party software overlays telemetry SDK rules screen capture" |
| Page reads | `POST /v1/scrape/website` | about 34 pages | vendor pricing and feature pages, iRacing and Cosworth pages, two aggregator roundups |
| Reddit | `POST /v1/scrape/reddit/search` | 2 succeeded, 1 failed (HTTP 502, query "AI coach iRacing", sort top, not retried) | r/iRacing and r/simracing, 60 posts listed |
| Reddit threads | `POST /v1/scrape/reddit/comments` | 8 threads, about 24 top comments each | the highest-signal threads on Trophi, Track Titan, Moza AI, "is AI coaching worth it" |
| Deep research | `POST /v1/research/deep` (raw mode) | 2 | iRacing and market size; funding and traction. Output was mostly search snippets and some noise, so I only used items with a source URL |
| YouTube | `POST /v1/scrape/youtube/search` | 1 (3 results) | thin; I did not pull transcripts |

Limitations to keep in mind:
- Reddit threads were chosen by relevance and score, not sampled randomly. Several people in the threads are vendors promoting their own tools (Track Impulse, mental apex racing, the Simulator Controller author, a Trophi product manager). I flag those.
- [PickMerit](https://pickmerit.com/blog/iracing-telemetry-and-coaching-apps) discloses that TakeP1 "is operated by the team behind PickMerit", so its roundups favor TakeP1. I used it for cross-checking, not as sole evidence.
- DeepAPI balance note: the account is now below the $5 warning level. `GET /v1/balance` showed $11.00 credits, $6.91 posted debits and $0.25 in active holds, so roughly $3.84 spendable now and about $4.09 once holds clear. Paid DeepAPI calls will start failing soon. Top-up page: https://deepapi.co/credits. I kept spending modest and skipped optional work (YouTube transcripts, a few extra pricing pages) partly to conserve it; further paid research (see §8) would need a top-up.

---

## 2. The landscape

### 2.1 Categories

1. **Live in-car AI coaches (direct competitors):** speak or overlay corrections while you drive. Trophi.ai, TheTenths, Apex Sim Coach, TakeP1, TrackPro, Track Impulse, Full Grip Vision, GoToPole, DeltaCoach, FlyLap, Bloops, RealRacer, RACEngineer.ai, PitWise, AI Chief, Simulator Controller.
2. **Post-session AI or telemetry coaches:** analysis after the lap or session. Track Titan, Coach Dave Delta, Race.Ninja, RaceCraft.ai, TrackLab, Telemetry Copilot.
3. **Conversational / debrief coaches:** chat with the AI about your session. mental apex racing, Race.Ninja chat, TrackPro (ask mid-corner), TheTenths debrief, Simulator Controller.
4. **Reference-lap and telemetry tools without AI:** Garage 61, VRS, Cosworth Pi Toolbox, MoTeC, Popometer.
5. **Human coaching with software:** Trophi Professional (Driver61 sessions), VRS coaching, Coach Dave coaching discount.
6. **Platform and big-tech entrants:** iRacing + Microsoft Research; generic screen-reading game assistants (Xbox Gaming Copilot, Google Play Games Sidekick) that are not racing-specific.

### 2.2 Product notes (each with source)

**Trophi.ai ("Mansell AI"), the best-known direct competitor**
- Live voice coach, "Real-Time Skill Analysis", Brake Point Assist, Pace Notes, live overlays, pro telemetry, lap analysis, iRacing weekly-series prep, time trials, minigames ([pricing page](https://www.trophi.ai/sim-racing/pricing/) [V]). Mansell speaks timed so advice lands as you reach the corner and "stays quiet" if there is no time ([Mansell page](https://www.trophi.ai/sim-racing/mansell/) [V]). It uses track landmarks ("100 board") and tracks "patterns across multiple laps" ([same](https://www.trophi.ai/sim-racing/mansell/) [V]).
- Pricing today [V]: Premium $89.99/yr (shown as $7.50/mo, list $179.88/yr); Premium + Setups (Grid & Go data packs) $199.99/yr; Team (3 licenses) $249.99/yr; Professional $699.99/yr including a monthly 45-minute Driver61 session and weekly group classes. 7-day free trial, 30-day money-back guarantee ([pricing](https://www.trophi.ai/sim-racing/pricing/)). PickMerit notes every plan is billed annually up front [S] ([PickMerit](https://pickmerit.com/blog/iracing-telemetry-and-coaching-apps)).
- Games: iRacing, ACC, LMU, F1 23/24/25 [V]. iRacing coverage is road only, ovals unsupported [S] ([Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/)).
- Self-reported rating 4.6/5 from 3,480 reviews [V, vendor claim, not audited].
- Funding: $3.3M seed (May 2023), per the company ([press release](https://www.trophi.ai/company/press/trophi-ai-seed-funding/)); a company page says "raised over $14M through venture backing and... programs" ([trophi.ai](https://www.trophi.ai/)) [S/U, snippet only].

**Track Titan** ("Strava for motorsport")
- Pricing [V] ([Track Titan pricing](https://app.tracktitan.io/pricing)): Community free forever (unlimited laps, telemetry without AI tips, track guides, "real-time coaching overlay"); Plus $7.99/mo (unlimited AI tips, thousands of pro reference laps); Setup $14.99/mo (Hymo setups); Ultra $19.99/mo. 7-day trial.
- Games include iRacing, AC, ACC, AC EVO, AMS2, Forza Motorsport, LMU, F1 20–25 [V] (same page). Post-session coaching, PC and console [S] ([Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/)).
- Traction and money: $5M seed announced 2025-12-04, co-led by Partech and Game Changers Ventures; "more than 200,000 users" and "ten-fold growth in ARR over the last two years" (company-reported) ([Partech](https://partechpartners.com/news/track-titan-raises-5-million-for-ai-powered-strava-for-motorsport)). A LinkedIn-derived aggregator lists 41 employees and about $1.1M annual revenue ([LinkedIn page via DeepAPI research](https://linkedin.com/company/tracktitan)) [U: third-party estimate, not confirmed].
- Also advertises an FIA collaboration (not endorsement) on its own pricing page [V].

**Coach Dave Delta**
- £11.99/mo or £109/yr, no free tier [V] ([pricing](https://coachdaveacademy.com/pricing/)). Bundles 1,000+ pro setups, "Setup IQ" tailor-made setups, AI "turn-by-turn feedback", racing-line / braking / throttle visualization, side-by-side lap compare, leaderboards, a driver course and coaching discounts [V] (same page).
- Desktop app, post-session AI ("Auto Insights") [S] ([Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/)).

**Garage 61** (no AI; the free benchmark everyone cites)
- Free during alpha; Pro is €5/mo when billed yearly and adds advanced filters and unlimited storage (free keeps one year) [V] ([Garage 61 Pro](https://garage61.net/pro), [FAQ](https://garage61.net/docs/faq/pricing)). The FAQ says "Not all of it" will stay free.
- Scale: "over 300 million laps" [V] (Pro page). A competitor-authored comparison says Garage 61 claims "more than 100,000 users" and is small and self-funded [S] ([Coach Dave comparison](https://coachdaveacademy.com/tutorials/coach-dave-delta-vs-garage-61-which-is-best-for-you/)).
- Sim Racing Manual calls it "the single most-recommended improvement tool" on r/iRacing and r/simracing [S] ([source](https://simracingmanual.com/resources/ai-driving-coaches/)).

**TheTenths** (free client, live voice coach "Powered by Claude")
- Reads telemetry and talks "corner by corner"; says it "learns on your own laps"; "Remembered between sessions"; habits and danger corners; briefing, debrief; corner overlay of your line vs your best with speed, brake and throttle traces; post-session analysis with reference of "the leaderboard's fastest, or an AI-estimated ideal lap" [V] ([TheTenths](https://www.thetenths.com/coaching)). Pricing: **[U]** (I only confirmed the coach runs inside "the free telemetry client").

**Apex Sim Coach**
- $9.99/mo after a 7-day trial; iRacing, AC, ACC "sold separately"; Windows; spoken coaching, post-session analysis, "Progress across sessions"; bundled local AI and neural voices, but "not completely offline" [V] ([Apex](https://apexsimcoach.com/)). The site marks its UI as "interface concept — not a product screenshot".

**TakeP1** (free)
- Live engineer voice, brake points, corner analysis, telemetry HUD, two-way radio ("ask it anything, mid-session"), post-session report, "mistake analyzer", voice cloning; 10 titles (9 on PC); Windows 10/11 or console; "no trial, no tiers, no card" [V] ([TakeP1](https://takep1.com/race-engineer/home)). It is operated by the PickMerit team [S] ([PickMerit](https://pickmerit.com/blog/iracing-telemetry-and-coaching-apps)).

**TrackPro by Sim Coaches**
- Live voice coach; "ask questions"; every session recorded; Windows; "full coaching free for 30 days"; 4.8 from 244 ratings (vendor claim) [V] ([TrackPro](https://ai.simcoaches.com/ai-sim-racing-coach)). Sim Racing Manual lists $20/mo for the AI voice coach and iRacing/AC/BeamNG only [S] ([source](https://simracingmanual.com/resources/ai-driving-coaches/)); I could not confirm the price on the vendor page.

**Full Grip Vision** (offline local coach): free Level 1; Supporter €3.99, Pro €7.99, Elite €9.99 per month; nine titles; no cloud [S] ([Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/)); vendor page confirms "All offline, all on your PC, across 9 games" and early access [V] ([Full Grip](https://www.fullgripmotorsport.com/about/fullgripvision)).

**Others (vendor pages unless noted)**
- **Track Impulse:** free overlays suite plus live voice coach ("Brake later, about 15 m, worth 0.3 s") and "Performance Review" post-session analysis; iRacing and LMU; "lifetime seconds found" per car and track ([Track Impulse](https://track-impulse.com/sim-racing-coach)). Price of the coach: **[U]**.
- **mental apex racing:** "Conversational AI coaching for iRacing", reads full telemetry, named corners, conditions, incidents, tyres and fuel, setup and what changed between stints, comparison laps from community pool or Garage 61, "What you said last time" and "Patterns from past s[essions]"; web and Windows app; free to start ([mental apex racing](https://mentalapexracing.com/)). Pricing beyond free: **[U]** (a Reddit user says 5 free debriefs/month, unverified).
- **FlyLap:** free overlay and AI coach; bring-your-own OpenRouter key; Garage 61 CSV references; audio brake-point beeps ([FlyLap](https://flylapsim.com/)).
- **GoToPole:** beta; Starter $0, Pro $19/mo, Team $59/mo; reference-lap-driven calls and corrections, trail-braking score, AI setup suggestions; local data; iRacing available ([GoToPole](https://gotopole.com/)).
- **DeltaCoach (Gordon AI):** iRacing-only beta, free now, "$49/mo" Pro listed as coming; 878 corners / 96 circuits mapped ([DeltaCoach](https://deltacoach.getgordonai.com/)). Unrelated to Coach Dave Delta.
- **RealRacer.ai:** waitlist; AUD $0 / $12.99 / $19.99 (AI coach joins) / $29.99 per month; iRacing, AC, LMU; phone and watch apps; claims to bridge sim and real-circuit coaching ([RealRacer](https://realracer.ai/)).
- **RACEngineer.ai:** early access, iRacing first, "Driver memory across sessions", local urgent calls plus cloud debrief ([RACEngineer.ai](https://racengineer.ai/)). Price **[U]**.
- **PitWise:** €3.99 one-time beta price (list €11.99), offline Whisper speech, optional bring-your-own LLM key, ACC-centric with iRacing listed ([PitWise](https://pitwise.net/)).
- **AI Chief:** iRacing "entertainment" persona; Free / $9.99 / $19.99 per month ([AI Chief](https://www.aichiefracing.com/)).
- **TrackLab Engineer:** iRacing-only; AI setup and hardware-settings recommendations, voice engineer, trace compare; plans in euros (€15/month up to €120/month appear on the page; tier names unclear) ([TrackLab](https://tracklab.gg/en)).
- **Virtual Paddock:** open beta, AI setup engineer and league steward, credit tiers $1–$6/mo ([Virtual Paddock](https://virtualpaddock.app/en/)).
- **RaceCraft.ai:** free beta, physics-based post-session analysis from .ibt uploads, iRacing and ACC ([RaceCraft.ai](https://racecraft.ai/)).
- **iRCopilot:** voice Q&A engineer; page says "paused for maintenance", subscriptions and downloads unavailable ([iRCopilot](https://www.ircopilot.com/)).
- **Race.Ninja:** AI coaching from uploaded video plus telemetry, clickable timestamps, chat follow-ups ([Race.Ninja](https://www.race.ninja/help/ai-coaching/how-does-ai-coaching-work)).
- **Simulator Controller:** free open source "virtual driving coach" and race engineer; you supply an LLM ([Sim Racing Manual](https://simracingmanual.com/resources/ai-driving-coaches/); author thread [r/simracing](https://www.reddit.com/r/simracing/comments/1h745li/ontrack_coaching_by_an_ai/)). Setup is technical.
- **Bloops:** free live reference-comparison overlay with braking audio cues, uses Garage 61 laps ([Simudex](https://simudex.com/software/bloops/), [Reddit praise](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)).
- **Cosworth Pi Toolbox:** official iRacing data-analysis partner; Lite free; paid Plus £5/mo (£50/yr), Pro £40/mo (£400/yr), plus an Ultra tier; real-time telemetry ([iRacing/Cosworth Nov 2025](https://www.iracing.com/cosworth-pi-toolbox-offers-more-data-real-time-telemetry-and-advanced-features-to-iracing-members/)).
- **VRS:** Free / $4.99 / $9.99 per month; human coaching from $29.99 per 30 min [S] ([PickMerit](https://pickmerit.com/blog/iracing-telemetry-and-coaching-apps)).
- **MOZA "AI Coach / Racing Lab":** AI that "actively control[s] compatible hardware" so you feel inputs through wheel and pedals ([MOZA](https://mozaracing.com/pages/ai-coach)). Community reaction in §5.

---

## 3. Feature matrix

"Y" = documented on vendor page [V] unless noted; "—" = not stated; "?" = could not confirm. Table A is features, Table B is sims, platform and money.

### Table A: features (iRacing-relevant products)

| Product | Live in-car coaching | Line viz / corner map | Brake / throttle / steer trace compare | Reference laps | Corner-by-corner | AI / NL chat | Long-term memory / progress | Setups |
|---|---|---|---|---|---|---|---|---|
| Trophi.ai | Voice + overlays | Track map, overlays | Y (pro telemetry, overlays) | Pro "expert", community laps | Y | Voice advice; Q&A not stated | Patterns across laps; progress not detailed | Add-on tier (Grid & Go) |
| TheTenths | Voice + corner overlay | Y (your line vs best) | Y | Leaderboard best or AI-estimated ideal | Y | Debrief; chat? | Y ("remembered between sessions", habits) | — |
| Apex Sim Coach | Voice | ? | ? | ? | ? | ? | Y ("progress across sessions") | — |
| TakeP1 | Voice + HUD | Racing-line markers | Y (HUD, delta) | Own best | Y | Two-way radio Q&A | Session and lap history | — |
| TrackPro | Voice | ? | ? | ? | Y | Y (ask questions) | Sessions recorded | — |
| Track Impulse | Voice + overlay | Y (corners drawn on road) | Y | ? | Y | ? | "Lifetime seconds found" | — |
| Full Grip Vision | Voice (offline) | ? | ? | ? | Y ("every corner") | ? | ? | Elite tier AI setup engineer |
| GoToPole | Voice calls | ? | Y (trail-brake score) | Own / shared | Y | ? | ? | AI setup suggestions |
| DeltaCoach (beta) | Voice (beta) | ? | Y (input analysis) | Own best of session | Y (3 phases) | ? | Y (3 priority corners carried to next session) | — |
| FlyLap | Overlay + beeps | Y (AI track map) | Y (11 channels) | Garage 61 CSV | Y | Structured LLM report, not chat | ? | — |
| mental apex racing | No (post-race) | ? | Via telemetry read | Community or Garage 61 | Y | **Y (conversational)** | **Y (past debriefs, patterns)** | Reads your setup |
| RACEngineer.ai | Voice (spotter, radio) | ? | ? | ? | ? | Push-to-talk answers | Y (driver memory) | — |
| Track Titan | Overlay in free tier; tips post-session | Y | Y | Pro laps (Plus) | Y | — | Y (laps, personal bests) | Y (paid) |
| Coach Dave Delta | No (post-session) | Y | Y | Pro + leaderboard | Y (Auto Insights) | — | Leaderboards, history | **Y (1,000+)** |
| Garage 61 | No | Y | Y | **300M+ laps community** | Sector / corner | No AI | Y (1 yr free, unlimited Pro) | Shared setups |
| Cosworth Pi Toolbox | Real-time telemetry (paid) | Y | Y | Ghost laps | Y | No | — | Setup validation |
| Race.Ninja | No | Y (video) | Y | Yes (own laps) | Y | **Y (chat)** | ? | — |
| MOZA AI Coach | Hardware demo | ? | ? | — | Staged training | ? | ? | — |

### Table B: sims, platform and pricing

| Product | Sims | Platform | Pricing / model |
|---|---|---|---|
| Trophi.ai | iRacing (road), ACC, LMU, F1 23–25 | Windows (+ console telemetry import [S]) | $89.99/yr ($7.50/mo) → $699.99/yr; 7-day trial; annual only [S] |
| TheTenths | ? (appeared in iRacing coach searches) | Windows client | Free client; coach pricing [U] |
| Apex Sim Coach | iRacing, AC, ACC (sold separately) | Windows | $9.99/mo after 7-day trial |
| TakeP1 | 10 titles incl. iRacing | Windows, console | Free, no card |
| TrackPro | iRacing, AC, BeamNG [S] | Windows | Free telemetry; AI coach about $20/mo [S]; 30-day trial [V] |
| Track Impulse | iRacing, LMU + more | Windows | Free download; coach price [U] |
| Full Grip Vision | 9 titles incl. iRacing | Windows, local | Free L1; €3.99 / €7.99 / €9.99 per month [S] |
| GoToPole | iRacing + ACC/rF2 coming | Windows | Beta: $0 / $19 / $59 per month |
| DeltaCoach | iRacing only | Windows + browser | Beta free; $49/mo planned |
| FlyLap | iRacing | Windows | Free; BYO OpenRouter key |
| mental apex racing | iRacing | Web + Windows | Free to start; paid [U] |
| RACEngineer.ai | iRacing first | Desktop | Early access; price [U] |
| Track Titan | iRacing, ACC, AC, AMS2, LMU, Forza, F1 | PC, console, iOS | Free; $7.99; $14.99 setups; $19.99 ultra |
| Coach Dave Delta | iRacing, ACC, LMU, GT7, AMS2, AC EVO | Windows (+ web review) | £11.99/mo or £109/yr; no free tier |
| Garage 61 | iRacing (mainly) | Windows agent + web (phone OK) | Free; Pro €5/mo yearly |
| Cosworth Pi Toolbox | iRacing | Windows | Lite free; Plus £5/mo; Pro £40/mo |
| Race.Ninja | multi | Web | ? |
| MOZA AI Coach | MOZA hardware | PC | Bundled with hardware [U] |

Observations from the matrix:
- **Live voice coaching and post-session analysis are both commodity.** Several are free.
- **Persistent, cross-session memory is claimed by only a few and described thinly** (TheTenths, Apex, RACEngineer.ai, mental apex racing, DeltaCoach's priority corners). None has a track record I could verify.
- **A real "chatbot after the race" exists only in early products** (mental apex racing, Race.Ninja, TrackPro mid-session Q&A, TakeP1 radio). No incumbent combines a live coach with a rich post-race conversation grounded in long history.
- **Racing-line visualization is mostly telemetry-derived** (position traces, track maps). I did not find any product that visually watches the screen to judge the line.
- **Setups are a separate, crowded market** (Coach Dave, Track Titan/Hymo, Trophi/Grid & Go, VRS, TrackLab, GoToPole) dominated by established shops.

---

## 4. The "sees your live screen" premise vs how competitors work

- **What competitors read:** the iRacing SDK. DeltaCoach: "Connects directly to iRacing via the official SDK... throttle, brake, steering, speed, and position in real time" ([DeltaCoach](https://deltacoach.getgordonai.com/)). GoToPole: "reads your sim's shared memory locally" ([GoToPole](https://gotopole.com/)). Apex: "grounded in live simulator telemetry" ([Apex](https://apexsimcoach.com/)).
- **Why that matters:** telemetry gives exact inputs, speed, gear and position (DeltaCoach cites 20 Hz), cheaply and with low latency. A screen model has to infer all of that from pixels and still cannot see pedal pressure. For "how to brake, throttle, line" telemetry is strictly better. (The sibling scout owns the technical detail.)
- **Where vision does appear:** [Race.Ninja](https://www.race.ninja/help/ai-coaching/how-does-ai-coaching-work) uses uploaded video plus telemetry with clickable timestamps; [Callup.gg](https://callup.gg/ai-coach/f1) reads an F1 clip; hobby repos and writeups attempt vision-only or OpenCV-based copilots ([PitLane AI](https://github.com/Adityaagulatii/cursor_hacker), [F1 Neural Copilot write-up](https://medium.com/@mnouira/building-a-local-privacy-first-f1-race-engineer-with-llama-3-whisper-and-opencv-f07750c14f75)); all are marginal and I verified none of their accuracy claims.
- **General-purpose screen-reading assistants exist** ([Xbox Gaming Copilot](https://news.xbox.com/en-us/2025/09/18/gaming-copilot-xbox-pc-mobile/), [Play Games Sidekick](https://9to5google.com/2025/09/23/play-games-sidekick-gemini-live/)) but they are not racing coaches. A hands-on review calls the Xbox console version's coaching "a bit of a grind" ([GamesRadar](https://www.gamesradar.com/platforms/xbox/xbox-says-copilot-on-consoles-provides-coaching-but-it-actually-talks-to-you-like-a-baby-with-a-controller-its-a-bit-of-a-grind-but-hey-youre-already-on-torment-4/)).
- **Possible genuine uses for vision:** racecraft context telemetry lacks (what the cars around you are doing, track edge and kerb use, replay review), sims or platforms without a usable telemetry API (console), and screenshot-level explanation. These are differentiator candidates, not MVP table stakes.
- **Policy risk:** iRacing "does not review, pre-approve, or grant authorization to third parties who develop software that works alongside the simulation", and may "restrict or remove access to various data at its discretion" ([iRacing support, modified 2026-10-01](https://support.iracing.com/support/solutions/articles/31000179757-third-party-development-please-review-the-iracing-terms) [V]). I did not read the full EULA for specific rules on screen capture or overlay injection, **[U]**; the sibling scout or a follow-up should.

---

## 5. User sentiment

Sources: 60 Reddit posts listed, about 190 comments read from 8 threads, plus review aggregators. Vendor self-promotion is flagged.

### 5.1 Trophi.ai (most discussed)

Praise:
- Real gains for newer or lower-iRating drivers and new tracks: "helped me visualize braking shapes", "best for... 1000-2000 IR range" ([r/iRacing 7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)). Another says it helped them move up to sports cars ([r/iRacing "Is AI coaching worth it?"](https://www.reddit.com/r/iRacing/comments/1x4hxaw/is_ai_coachingtelemetry_worth_it/)).
- Overlays and brake minigames valued even by people who ignore the voice: "telemetry overlays literally save me 10–15 laps at every new class/track combo" ([r/iRacing "I tested a Professional Coach against Trophi AI"](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)).
- Cancellation praised as easy: "two clicks and it was done so they have my respect" ([r/iRacing which-tool thread](https://www.reddit.com/r/iRacing/comments/1wt6v2b/coach_dave_track_titan_trophiai_g61_which_is_the/)).

Complaints (repeated):
- **Shallow, repetitive advice:** "feedback was largely brake X meters later/earlier" ([r/iRacing worth-it thread](https://www.reddit.com/r/iRacing/comments/1x4hxaw/is_ai_coachingtelemetry_worth_it/)); "all it cared about was my braking point... even if I purposely did zero trailbraking, missed the apex or had bad exits" ([r/iRacing which-tool thread](https://www.reddit.com/r/iRacing/comments/1wt6v2b/coach_dave_track_titan_trophiai_g61_which_is_the/)).
- **Unstable or wrong reference:** "brake 50m earlier then the next lap brake 30m later... I couldn't understand the reference point" ([7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)). A commenter identifying as a Trophi product manager acknowledged the coach compares against your last lap, "which doesn't give a stable anchor", and said they are working on it (same thread; identity unverified). Others complain the "alien" lap is too slow ([r/iRacing pro-coach test thread](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)).
- **Performance and trust:** the highest-scoring thread (217 points) reports rising CPU and network load that the poster tied to a Trophi background process and an incident in an endurance race ([r/simracing "WARNING to anyone using Trophi AI"](https://www.reddit.com/r/simracing/comments/1jhuw6l/warning_to_anyone_using_trophi_ai/)); other commenters disputed the cause and noted Trophi itself says overlays are for practice only. A 2024 thread is titled "Can anyone explain this to me. Trophi ai trojan?" ([r/simracing](https://www.reddit.com/r/simracing/comments/1acnqp5/can_anyone_explain_this_to_me_trophi_ai_trojan/)); I saw only the title.
- **Setup friction:** needs windows and Open Kneeboard configured each time, especially in VR ([7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/), [worth-it thread](https://www.reddit.com/r/iRacing/comments/1x4hxaw/is_ai_coachingtelemetry_worth_it/)).
- **Silence is ambiguous:** it says nothing when you are "on point", so users cannot tell "good" from "broken" ([7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)).
- **Reliability / support:** a long-term subscriber got six free months because "the app [was] very buggy and unusable" ([pro-coach test thread](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)); another could not load the telemetry file they wanted coaching against (same thread).
- **Trial behavior:** two users report being offered a second 7-day trial after cancelling ([7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)), a sign of retention difficulty (anecdote).

### 5.2 Track Titan, Coach Dave Delta, Garage 61, VRS

- **Coach Dave** is called "the best at the analysis side", with the in-lap reference video "very handy" ([r/iRacing which-tool thread](https://www.reddit.com/r/iRacing/comments/1wt6v2b/coach_dave_track_titan_trophiai_g61_which_is_the/)); one user calls it "essentially a paid version of G61 that comes with coach dave setups" (same thread). A beginner preferred it because you "lap in silence and let it record data" (same thread).
- **Track Titan:** "great if you're willing to sit for a couple hours and really understand your data" (same thread); a paid user liked synced video and an iOS app (same thread). A r/simracing thread titled "The reason why Track Titan was decoupled from Pit House - Moza AI" (53 points, 74 comments) is almost entirely hostile to Moza's AI-controls-your-wheel feature ([r/simracing](https://www.reddit.com/r/simracing/comments/1rpd3o8/the_reason_why_track_titan_was_decoupled_from_pit/)); I could not tell from the comments how Track Titan is involved, so I do not count it as Track Titan sentiment. A separate r/iRacing thread titled "Someone might need to check on the person running Track Titan..." has 38 points; I read only the title ([r/iRacing](https://www.reddit.com/r/iRacing/comments/1uo3xf7/someone_might_need_to_check_on_the_person_running/)).
- **Garage 61:** "good if you know how to interpret the data" and "all I see is lines on my screen" (same thread). Telemetry literacy is the barrier AI is supposed to remove.
- **Consistent consensus:** "Nothing will ever beat a real coach as you can ask questions back" (same thread); "Best for improvement? A 1-hour session with an actual coach" (same thread). Sim Racing Manual puts human sessions at roughly $50–100 each and says AI "plateau[s] the moment you can read your own telemetry" ([source](https://simracingmanual.com/resources/ai-driving-coaches/)).

### 5.3 The free stack is the real competitor

- "You can do most of what these 'AI' programs do for free using Garage61 and Bloops." ([worth-it thread](https://www.reddit.com/r/iRacing/comments/1x4hxaw/is_ai_coachingtelemetry_worth_it/), [7-day trial thread](https://www.reddit.com/r/iRacing/comments/1p20e4q/started_the_7_day_trial_of_trophiai_detailing_my/)) and "Bloops is better than Trophi.ai... Trophi.ai has better marketing" (worth-it thread; one user's opinion).
- **Do-it-yourself LLM workflow:** "Garage61 for telemetry analysis, Bloops for in-game telemetry overlays..., and Claude (or any LLM really) for detailed telemetry comparison and feedback"; another uploads screenshots of Garage 61 to Gemini (worth-it thread). People are already building cocoach's post-race chat by hand, which validates the need and sets a free baseline.

### 5.4 Anti-AI backlash (feature to avoid)

- Reaction to MOZA's AI that controls the wheel and pedals: "AI taking over your wheel and pedals has absolutely no place in sim racing", "I would pay extra to not have this feature", "Nah. I think it's time I heavily reconsider buying MOZA products" ([r/simracing](https://www.reddit.com/r/simracing/comments/1rpd3o8/the_reason_why_track_titan_was_decoupled_from_pit/)). Others say "AI analyzing laps based on telemetry... I am fine with". Position AI as coach and analyst, never autopilot. Cheating concerns appear in the same thread.

### 5.5 Unmet needs that recur

1. **Context and "why"**, not just numbers: "coaching lives and dies on context" and "my problem with this AI product is that it sometimes pretends to have more insight than it actually does" (pro-coach thread quotes). Users want a human-like assessment of level, habits and mental game ([pro-coach thread, coach comment](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)).
2. **A stable, relevant, choosable reference** (any lap, matched conditions, an attainable level) and coaching against "an average lap rather than your best" ([worth-it thread](https://www.reddit.com/r/iRacing/comments/1x4hxaw/is_ai_coachingtelemetry_worth_it/)).
3. **Post-race review and an "AI crew chief"** ([pro-coach thread](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)); race-engineer features requested in a 70-comment thread were met with skepticism that most drivers need one ([r/iRacing "Race Engineers"](https://www.reddit.com/r/iRacing/comments/1mgxt83/race_engineers/)).
4. **Ovals:** a post titled "AI oval coach" exists ([r/iRacing](https://www.reddit.com/r/iRacing/comments/1xkradb/ai_oval_coach/), title only), and Trophi does not cover iRacing ovals [S].
5. **Lightweight, VR-friendly delivery** (voice-first, no fiddly overlay windows).
6. **Accessible to beginners:** "AI info saying 'brake 10% less on turn 5' didn't mean [anything] because I didn't know the track" ([which-tool thread](https://www.reddit.com/r/iRacing/comments/1wt6v2b/coach_dave_track_titan_trophiai_g61_which_is_the/)).
7. **Subscription fatigue:** one beta tester stopped once it went public because they do not want another subscription ([pro-coach thread](https://www.reddit.com/r/iRacing/comments/1qar0cv/i_tested_a_professional_coach_against_trophi_ai/)).

### 5.6 YouTube

Search returned only three results ([Sarah Nocchi "AI Coaching in Sim Racing: Game Changer or Gimmick?"](https://www.youtube.com/watch?v=cpa_JJ2fssk), 22,194 views; [Moore Podiums trophi.ai review](https://www.youtube.com/watch?v=nnd-gHL1n2I); one unrelated). I did not pull transcripts or comments, so YouTube sentiment is **[U]**. Trophi's pricing page features creator praise, which is vendor-selected marketing ([Trophi](https://www.trophi.ai/sim-racing/pricing/)).

---

## 6. Opportunity sizing

Credible signals:

| Signal | Value | Source |
|---|---|---|
| iRacing community size | "over 350,000 sim racers" | [iracing.com](https://www.iracing.com/) [V] |
| Same figure in a partner press release | "more than 350,000 active sim racers" (Aug 2025) | [iRacing/Cosworth](https://www.iracing.com/cosworth-and-iracing-unite-to-redefine-data-analysis-in-sim-racing/) [V] |
| Older figure | "more than 225,000 active users" | [FIA](https://www.fia.com/news/fia-and-iracing-accelerate-efforts-grow-grassroots-motor-sport) [S] |
| Subscriber and revenue figures | iRacing does not publish regular subscriber, renewal or churn numbers after its August 2026 price rise | [Box This Lap](https://boxthislap.org/has-iracings-2026-price-increase-hurt-the-service/) [S, via DeepAPI snippet] |
| iRacing pricing | price rose 2026-08-18: 1 month $14.75, 3 months $37.50, 12 months $124.50 | [iRacing](https://www.iracing.com/iracing-subscription-pricing-update-july-2026/), [Traxion](https://traxion.gg/iracing-set-to-increase-subscription-costs/) [S] |
| Activity | "over 10 million driver entries across 734,000 events in Season 1 of 2026" | [iRacing Facebook](https://www.facebook.com/iracing/posts/iracing-had-over-10-million-driver-entries-across-734000-events-in-season-1-of-2/1394017476097809/) [S] |
| Competitor traction | Track Titan 200k+ users, $5M seed (Dec 2025) | [Partech](https://partechpartners.com/news/track-titan-raises-5-million-for-ai-powered-strava-for-motorsport) (company-reported) |
| Competitor traction | Trophi $3.3M seed (2023); "10,000 users following alpha launch... path to 100,000 by 2024" (company projection) | [Trophi press](https://www.trophi.ai/company/press/trophi-ai-seed-funding/) |
| Competitor traction | Garage 61: "over 300 million laps"; "more than 100,000 users" claimed | [G61 Pro](https://garage61.net/pro), [Coach Dave comparison](https://coachdaveacademy.com/tutorials/coach-dave-delta-vs-garage-61-which-is-best-for-you/) (competitor-authored) |
| Wider market reports | "racing simulator market" $1.0–1.4B (2026); forecasts vary widely | [Fortune Business Insights](https://www.fortunebusinessinsights.com/racing-simulator-market-115926), [Market Research Future](https://www.marketresearchfuture.com/reports/racing-simulator-market-21942) [S, low quality, mostly hardware] |
| Cosworth tiers | Pi Toolbox Plus £5/mo, Pro £40/mo | [iRacing](https://www.iracing.com/cosworth-pi-toolbox-offers-more-data-real-time-telemetry-and-advanced-features-to-iracing-members/) [V] |

My bottom-up estimate (arithmetic from the figures above, **not sourced data; treat as a hypothesis to test**):
- Addressable iRacing users: about 350k. If 3–10% pay for a coaching tool at about $8/month ($96/year): roughly 10,500–35,000 payers, or about **$1.0M–$3.4M ARR** for the whole iRacing AI-coaching segment at one price point. Any single vendor gets a fraction.
- The segment is shared with free options (TakeP1, Garage 61 + Bloops, Track Titan Community), so realistic conversion may be below that range.
- Track Titan's reported ~$1.1M revenue with 200k users across many sims [U] is consistent with the low end.
- Growth levers outside iRacing: multi-sim support (Track Titan, TakeP1 and others already do this), console, real-track-day coaching (RealRacer, Track Titan both target it), and teams or leagues (Trophi Team plan $249.99/yr for 3 licenses).

Takeaway: a viable indie or small-team business, not a venture-scale market on iRacing alone. Platform risk (§0 item 5) can cap the upside.

---

## 7. Gaps and positioning for cocoach

### 7.1 Table stakes for an MVP (what users expect because incumbents ship it)

1. **Reads iRacing telemetry reliably** and coaches during practice with **one clear, well-timed correction at a time**, silent when there is no time to speak (Trophi, Track Impulse, TheTenths all do this).
2. **A stable, user-selectable reference lap:** your best, a Garage 61 import, or a community lap. This is the top Trophi complaint.
3. **Post-session debrief with corner-by-corner time loss** and a track map showing where.
4. **Low overhead:** negligible CPU, no mandatory heavy overlays, works in VR. Voice-first. A code-signed installer (TakeP1's unsigned installer trips SmartScreen per [PickMerit](https://pickmerit.com/blog/iracing-telemetry-and-coaching-apps) [S]).
5. **Free tier and trial; easy cancellation.** The category's anchors are $0 products and a 7-day trial.
6. **Windows desktop client** (most competitors are Windows-first; iRacing is a PC sim).

### 7.2 Differentiators worth building

1. **Post-race chatbot with persistent memory** (the captain's ask, and the clearest gap): remembers your goals, recurring faults, what you tried last session and whether it worked; answers "why" in plain language; grounded in the telemetry, not a generic LLM. Evidence of demand: users hand-build this with Garage 61 + Claude/Gemini, and mental apex racing and RACEngineer.ai are launching exactly this.
2. **Coaching that adapts to skill level and explains the cause** (beginner vs 4k iR), avoiding the "brake X m later" default and never bluffing: state confidence, and say "I don't know" when telemetry is insufficient.
3. **Racecraft and race-context coaching** (starts, traffic, restarts, incidents, tyre and fuel trends), where lap-based tools are weak. Pair with an honest after-race review of incidents.
4. **Better references:** matched-conditions community laps, "next level up" targets and average-lap-vs-best comparisons instead of a single "alien" lap.
5. **Oval coverage** if technically feasible. Trophi does not cover ovals [S]; demand signals are thin ([title only](https://www.reddit.com/r/iRacing/comments/1xkradb/ai_oval_coach/)).
6. **Vision as an enrichment, not the core** (see §4): e.g. replay and onboard review with timestamped notes (Race.Ninja pattern), racecraft awareness telemetry cannot see.
7. **Optional bring-your-own-model mode** (FlyLap, PitWise, Simulator Controller already offer it) as a power-user and cost-control lever.

### 7.3 Features to avoid

- **AI that controls the wheel or pedals**: strong backlash ([MOZA thread](https://www.reddit.com/r/simracing/comments/1rpd3o8/the_reason_why_track_titan_was_decoupled_from_pit/)).
- **Setups as a core product:** crowded, bundled by Coach Dave (1,000+), Track Titan/Hymo, Trophi/Grid & Go, VRS; users already buy them from shops and Discords (see [r/iRacing "Race Engineers"](https://www.reddit.com/r/iRacing/comments/1mgxt83/race_engineers/)). Integrate or partner later, do not build in-house.
- **Heavy always-on overlays during races** (Trophi's CPU controversy).
- **A human coaching marketplace** in the MVP. High-touch, and Trophi/VRS already bundle it.
- **Entertainment personas as the headline** (AI Chief's "savage personality"). Fine as a skin, not a product.
- **Metered, surprise-bill LLM usage for the end user.** Absorb inference costs behind rate limits; offer BYO-key as an option.
- **Blocking dependencies on iRacing's private or unapproved APIs** (iRacing grants no authorization to third parties, see §4).

### 7.4 Pricing stance (suggestion, with reasoning; to be tested)

- **Free tier that is genuinely useful:** telemetry capture, a post-race recap and a limited number of chat debriefs per month. Reason: the competitive floor is free (TakeP1, Garage 61, Track Titan Community, Bloops, FlyLap) and users say "I don't need another subscription".
- **One paid tier at about $8/month (or about $70–80/year):** unlimited chat and memory, live coaching, advanced references. Reason: it sits just above Trophi's effective $7.50 and Track Titan Plus $7.99, below Coach Dave £11.99 and Apex $9.99, and well under the $14.75/month people already pay iRacing. Price above about $10 only once outcomes are proven.
- **Monthly and annual billing, 14-day trial, one-click cancel.** Trophi is reportedly annual-only [S]; easy cancellation is a trust win users call out.
- **Optional higher tier later** (team/coach seats, human-in-the-loop review). Trophi Team is $249.99/yr for 3 licenses.
- **Hypothesis to validate before launch:** willingness to pay for memory and chat specifically (not live voice). Evidence is indirect; run a waitlist or landing-page test.

---

## 8. Caveats and open items

- Unverified: TheTenths, Track Impulse coach, mental apex racing and RACEngineer.ai paid pricing; Trophi and Garage 61 real user counts; Moza AI Coach pricing and exact behavior beyond its page; Track Titan $1.1M revenue (aggregator); TrackLab tier names; whether iRacing's EULA specifically forbids or allows screen capture or overlays; details of iRacing's Microsoft Research project beyond the official one-sentence update.
- Reddit sample is biased toward threads that mention the big names; several commenters are vendors.
- Market-size reports are low quality and hardware-heavy. The bottom-up estimate in §6 is mine.
- iRacing says "sim racers" and "active users"; it does not break out paid subscribers.
- Suggested follow-ups if the captain wants more depth (each needs a DeepAPI top-up): pull YouTube transcripts for the top three reviews; read the full iRacing EULA and Sporting Code for overlay and screen-capture rules; verify TheTenths and mental apex racing pricing; check Garage 61 API terms (FlyLap, Bloops, mental apex racing all depend on its exports); monitor iRacing's Microsoft Research coaching project.

## 9. Recommendation summary

Proceed only if the team accepts the platform-risk and low-ceiling economics. If proceeding: telemetry-first MVP with (1) a lightweight live voice coach, (2) a post-race chat coach with persistent, telemetry-grounded memory as the headline differentiator, (3) selectable references. Add vision only for what telemetry cannot see. Free tier plus about $8/month. Avoid setups, autopilot-style AI and heavy overlays.

If findings reveal shippable work: none; this is a research deliverable only.
