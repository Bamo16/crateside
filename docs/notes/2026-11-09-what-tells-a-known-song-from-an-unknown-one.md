# Which free, keyless signals separate songs a listener knows from songs they don't

Measured 2026-11-09 against the ledger's familiarity answers and nine further labels from the maintainer. Supports [ADR 0089](../adr/0089-familiarity-is-read-off-the-songs-own-listeners-and-its-wikipedia-article.md).

## Headline

Base rate: 30 of 52 are NOT, so always guessing NOT is 58% right. Everything below should be read against that, on 52 songs.

| Signal | Coverage | Accuracy at its best cut (covered) | Leave-one-out | AUC |
|---|---|---|---|---|
| ListenBrainz distinct users, >= ~3,500 | 46/52 | 87% | 85% | 0.86 |
| ListenBrainz total listens, >= ~26,000 | 46/52 | 85% | 83% | 0.83 |
| Wikipedia artist pageviews, >= ~88k/yr | 40/52 | 88% | 85% | 0.90 |
| Wikipedia song pageviews, >= ~20k/yr (0 = no article) | 52/52 | 85% | 77% | 0.84 |
| Song has an English Wikipedia article (yes/no) | 52/52 | 81% | 81% | 0.80 |
| Deezer track rank, >= ~430k | 41/52 | 76% | 68% | 0.79 |
| Deezer artist fans, >= ~39k | 50/52 | 70% | 68% | 0.71 |
| Wikidata `charted in` (P2291) | 0 songs | unusable: no song has it | - | - |
| Last.fm | skipped, no key | - | - | - |

- **Best song-level signal: ListenBrainz distinct users** (87%), with the best coverage-versus-accuracy trade: the 6 songs with no ListenBrainz value are all NOT, so reading "no value" as NOT scores 88% over all 52. The cheapest alternative with full coverage and no threshold is **"the song has its own English Wikipedia article"** (81%); it needs one lookup and never abstains.
- **Deezer is the weakest here**, which is what the existing Familiarity gate rests on. Fans punishes old hits by small acts (Biz Markie 12k, Bow Wow Wow 4k, Craig Mack 8k are KNOWN) and rewards known artists' unknown songs (Disclosure *Waterfall*, Basement Jaxx, Flume, aespa *Spicy*). Track rank misses the Kreayshawn songs and Belinda Carlisle.
- **Artist-level signals (artist pageviews, artist article, Deezer fans) cannot tell a known artist's unknown song from their hit.** Artist pageviews has no missed KNOWN songs at its cut but calls 5 NOT songs KNOWN, all by well-known artists (Disclosure *Waterfall*, aespa *Spicy*, Flume, two Basement Jaxx). It also only has a value for 40 songs, and the 12 without one are almost all small acts (Busy P is missed only because his article is titled Pedro Winter).
- **A few songs defeat every signal**: Bomfunk MC's *Freestyler* (NOT, huge on every measure), aespa *Spicy*, Basement Jaxx *Good Luck*, Bodyrox *Yeah Yeah* (NOT but popular); Kreayshawn *Bumpin Bumpin*, Disclosure *NO CAP*, 2 Unlimited *Get Ready* (KNOWN but with little online footprint). These are one listener's idiosyncrasies and are probably the floor of what a public signal can do.
- **Wikidata P2291 does not exist for songs in practice**, even for Rolling in the Deep. A chart signal would have to come from Wikipedia article text (79% as a text proxy, no better than "has an article"), not Wikidata.
- With 52 songs, 8 of them one artist, the gap between 85% and 87% is one song. The ordering "ListenBrainz and Wikipedia beat Deezer" (about 10 points, 5 to 7 songs) is the only difference I would trust, and even that rests on this one listener's labels.

## Sample

- 54 labelled rows: 45 from the ledger's `Settled` answers (Mainstream = KNOWN, Obscure = NOT) plus the 9 from the maintainer (6 KNOWN, 3 NOT).
- Evaluated on **52 songs: 22 KNOWN, 30 NOT.** Excluded: `kreayshawn - gucci gucci (acapella)`, `unK1yGbL9nk` (the acapella row is the same song as `kreayshawn - gucci gucci`; `unK1yGbL9nk` is a bare video ID with no artist or title to look up).
- This is tiny and skewed. 8 of the 30 NOT songs are one artist (Fox Stevenson); the KNOWN songs are mostly 90s/00s hits and the NOT songs mostly niche electronic, J-pop and doujin music. The two maintainer groups are different questions ("the listener knows it" vs "a mashup audience would"), pooled here as asked. Cuts are fitted on the same rows they are scored on, so accuracies are optimistic; the leave-one-out column is the honest number. Differences of one or two songs between signals are noise.
- Two Deezer caveats: rows the ledger had no Deezer data for were filled from the public search (exact artist name, title similarity >= 0.85, highest-ranked match); Deezer's search could not find some real artists' catalogues at all (Adele's own tracks never surfaced from free-text search; they came from the exact-name artist's top-100 list). Fans marked with a dagger are an exact-name artist match with no track match, so they may belong to a different artist of the same name.
- Last.fm: `LASTFM_API_KEY` is not set in this environment, so Last.fm was skipped.
- Wikidata P2291 ("charted in") is **absent on every song item**, including the largest ones (Rolling in the Deep Q170165, Bye Bye Bye Q3647850, Gypsy Woman Q838912 carry no P2291 at all). It is not usable as a signal. As a stand-in I also record whether the song's English Wikipedia article text names a chart (Billboard Hot 100, UK Singles Chart, Official Charts, ARIA, Oricon or a generic "Singles Chart"); that is a Wikipedia-text proxy, not what was asked for.
- ListenBrainz: the best-listened recording among up to 100 MusicBrainz hits whose title (parentheticals stripped) is >= 0.85 similar and whose artist credit contains the artist. Where MusicBrainz or ListenBrainz knows nothing the value is `-` (no value), distinct from a real 0. Pageviews are all-access, user agent, Nov 2025 to Oct 2026.

## Every labelled song

Columns: Lbl K/N = KNOWN/NOT; DzRank = Deezer track rank; Fans = Deezer artist fans; LB L/U = ListenBrainz listens / users; SongWP = song article exists; SongPV = its 12-month pageviews; ArtPV = artist article 12-month pageviews; Chart = article text names a chart; P2291 = Wikidata charted-in statements. `-` = no value. `src` m = maintainer-supplied label.

| Song | Lbl | DzRank | Fans | LB L | LB U | SongWP | SongPV | ArtPV | Chart | P2291 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1991 - if only | N | 314,320 | 5,026 | 927 | 254 | n | - | - | - | - |
| 2 unlimited - get ready | K | 534,031 | 68,470 | 2,968 | 854 | n | - | 97,519 | - | - |
| aiobahn feat. ナナヲアカリ - しあわせになんてならないで | N | - | 3,508† | 4,923 | 433 | n | - | - | - | - |
| andrew dum, alina eremia, café del mundo - paradise (radio edit) | N | 335,877 | 81,031 | - | - | n | - | - | - | - |
| angel mina - turn me up until i'm gone | N | - | - | 20 | 10 | n | - | - | - | - |
| basement jaxx - never say never (extended version) | N | 274,151 | 93,118 | 2,741 | 674 | Y | 1,719 | 114,612 | n | 0 |
| basement jaxx, lisa kekaula - good luck | N | 363,335 | 93,118 | 257,059 | 39,037 | Y | 6,736 | 114,612 | Y | 0 |
| belinda carlisle - heaven is a place on earth | K | 247,477 | 39,055 | 353,235 | 51,850 | Y | 119,681 | 859,810 | Y | 0 |
| biz markie - just a friend | K | 464,241 | 12,167 | 175,051 | 33,304 | Y | 38,552 | 270,632 | Y | 0 |
| black box - everybody everybody | K | 452,581 | 20,620 | 26,008 | 6,921 | Y | 22,475 | 87,632 | Y | 0 |
| bloodhound gang - the bad touch | K | 683,784 | 130,469 | 148,211 | 18,233 | Y | 85,697 | 374,811 | Y | 0 |
| bodyrox, luciana - yeah yeah (d ramirez vocal radio edit) | N | 416,283 | 6,316 | 87,636 | 8,505 | Y | 6,676 | 3,917 | Y | 0 |
| bomfunk mc's - freestyler | N | 625,708 | 13,522 | 360,136 | 48,401 | Y | 76,640 | 61,127 | Y | 0 |
| bow wow wow - i want candy | K | 495,625 | 4,185 | 132,261 | 31,200 | Y | 73,885 | 200,601 | Y | 0 |
| chingy - right thurr | K | 588,505 | 92,896 | 90,865 | 18,899 | Y | 21,329 | 201,436 | Y | 0 |
| clozee, barbatuques - baiana (clozee remix) | N | 458,615 | 26,118 | - | - | n | - | 9,480 | - | - |
| craig mack - flava in ya ear | K | 477,969 | 7,533 | 39,189 | 8,515 | Y | 31,156 | 629,825 | Y | 0 |
| crystal waters - gypsy woman (she's homeless) (la da dee la da da) ... | K | 525,743 | 18,731 | 49,987 | 10,228 | Y | 72,898 | 117,774 | Y | 0 |
| death grips - takyon (death yon) | K | - | 62,579† | 113,656 | 17,626 | n | - | 349,205 | - | - |
| flume - what you need | N | 164,093 | 376,436 | 6,161 | 1,255 | n | - | 106,271 | - | - |
| fox stevenson - all eyes on me | N | 198,118 | 30,317 | 10,301 | 906 | n | - | 15,693 | - | - |
| fox stevenson - bring a coat | N | 53,170 | 30,317 | 1,917 | 201 | n | - | 15,693 | - | - |
| fox stevenson - don't care crown | N | 228,322 | 30,317 | 9,940 | 749 | n | - | 15,693 | - | - |
| fox stevenson - give me some space | N | 162,704 | 30,317 | 2,741 | 314 | n | - | 15,693 | - | - |
| fox stevenson - glue gun | N | 162,941 | 30,317 | 4,046 | 421 | n | - | 15,693 | - | - |
| fox stevenson - human in the evening | N | 279,682 | 30,317 | 4,743 | 405 | n | - | 15,693 | - | - |
| fox stevenson - road to nothing | N | 153,979 | 30,317 | 2,682 | 301 | n | - | 15,693 | - | - |
| fox stevenson - that choice | N | 159,107 | 30,317 | 4,941 | 471 | n | - | 15,693 | - | - |
| gyari (ココアシガレットp) - アカリがやってきたぞっ | N | - | 45† | - | - | n | - | - | - | - |
| gyari (ココアシガレットp) - チャンネル登録よろしくお願いします | N | - | 45† | - | - | n | - | - | - | - |
| kreayshawn - bumpin bumpin | K | 184,098 | 26,618 | 1,113 | 234 | n | - | 97,827 | - | - |
| kreayshawn - go hard (la.la.la) | K | 336,506 | 26,618 | 57,529 | 7,099 | Y | 2,034 | 97,827 | n | 0 |
| kreayshawn - gucci gucci | K | 389,480 | 26,618 | 280,622 | 31,186 | Y | 20,461 | 97,827 | Y | 0 |
| kreayshawn - gucci gucci (acapella) *(excluded)* | K | 389,480 | 26,618 | 280,622 | 31,186 | Y | 20,461 | 97,827 | Y | 0 |
| mitsukiyo - unwelcome school | N | - | 191† | - | - | n | - | - | - | - |
| nimino - orla | N | 416,599 | 3,596 | 1,016 | 324 | n | - | 21,048 | - | - |
| nimino, manta - better | N | 519,445 | 3,596 | 2,524 | 734 | n | - | 21,048 | - | - |
| nokbient, tiny waves - opinions | N | - | 39† | 24 | 5 | n | - | - | - | - |
| nokbient, tiny waves - pfeilspitze | N | - | 39† | 25 | 5 | n | - | - | - | - |
| nsync - bye bye bye | K | 505,100 | 311,089 | 350,780 | 50,956 | Y | 140,199 | 861,665 | Y | 0 |
| run the jewels - legend has it | K | 430,856 | 63,114 | 42,602 | 4,259 | Y | 3,739 | 178,590 | n | - |
| run the jewels - ooh la la (feat. greg nice & dj premier) | K | 251,350 | 96,067 | 27,475 | 3,481 | n | - | 178,590 | - | - |
| unK1yGbL9nk *(excluded)* | N | - | - | - | - | n | - | - | - | - |
| 아야츠노 유니 (ayatsuno yuni) - supadopa | N | - | - | - | - | n | - | - | - | - |
| 電音部 - eat sleep dance (feat. moe shop) | N | - | 108† | 4,607 | 500 | n | - | - | - | - |
| Creepy Nuts - Bling-Bang-Bang-Born | Km | 732,512 | 49,849 | 51,860 | 4,530 | Y | 39,689 | 87,588 | Y | 0 |
| Disclosure - Latch | Km | 811,242 | 987,727 | 53,474 | 6,368 | Y | 49,928 | 117,497 | Y | 0 |
| Disclosure - NO CAP | Km | 440,240 | 987,727 | 1,597 | 559 | n | - | 117,497 | - | - |
| Adele - Hello | Km | 698,953 | 15,519,926 | 71,667 | 8,248 | Y | 289,222 | 1,767,528 | Y | 0 |
| Adele - Rolling in the Deep | Km | 745,639 | 15,519,926 | 4,030,293 | 234,367 | Y | 119,530 | 1,767,528 | Y | 0 |
| Adele - Someone Like You | Km | - | 15,519,926† | 2,939,088 | 183,030 | Y | 82,251 | 1,767,528 | Y | 0 |
| aespa - Spicy | Nm | 538,392 | 471,157 | 31,061 | 1,660 | Y | 6,325 | 589,195 | Y | - |
| Busy P - Genie | Nm | 443,090 | 67,407 | 439 | 132 | n | - | - | - | - |
| Disclosure - Waterfall | Nm | 363,290 | 987,727 | 5,398 | 1,027 | n | - | 117,497 | - | - |

Songs matched to wrong things were checked by hand: `Disclosure - Waterfall` initially matched the Stone Roses' *Waterfall* article and was reset to no article (Disclosure's *Waterfall* has none); `run the jewels - legend has it` was accepted on title and artist without a Wikidata performer link.

## Per signal

Rule is value >= cut means KNOWN. Cut = threshold maximising balanced accuracy on the covered songs. "Cov" is how many of the evaluated songs have a value; accuracy is on those only. "Missing=NOT" is the accuracy over all songs if a missing value is read as not-known. LOO = leave-one-out accuracy of the cut-fitting procedure on covered songs.

| Signal | Cov | AUC | Best cut | Bal. acc | Acc (covered) | LOO acc | Acc, missing=NOT | Errors |
|---|---|---|---|---|---|---|---|---|
| Deezer track rank | 41/52 | 0.79 | >= 430,856 | 76% | 76% (31/41) | 68% | 77% | 10 |
| Deezer artist fans | 50/52 | 0.71 | >= 39,055 | 69% | 70% (35/50) | 68% | 71% | 15 |
| ListenBrainz listens | 46/52 | 0.83 | >= 26,008 | 85% | 85% (39/46) | 83% | 87% | 7 |
| ListenBrainz users | 46/52 | 0.86 | >= 3,481 | 87% | 87% (40/46) | 85% | 88% | 6 |
| Wikipedia song pageviews 12 mo (0 if no article) | 52/52 | 0.84 | >= 20,461 | 82% | 85% (44/52) | 77% | 85% | 8 |
| Wikipedia artist pageviews 12 mo | 40/52 | 0.90 | >= 87,588 | 86% | 88% (35/40) | 85% | 90% | 5 |
| Song has Wikipedia article (1/0) | 52/52 | 0.80 | >= 1 | 80% | 81% (42/52) | 81% | 81% | 10 |
| Artist has Wikipedia article (1/0) | 52/52 | 0.70 | >= 1 | 70% | 65% (34/52) | 65% | 65% | 18 |
| Song article mentions a national chart (1/0) | 52/52 | 0.77 | >= 1 | 77% | 79% (41/52) | 79% | 79% | 11 |
| Wikidata P2291 count | 52/52 | - | - | - | - | - | - | every value is 0: no song item has a P2291 statement (Wikidata items were found for 21 songs; none has P2291) |
| Combo: article OR LB listens >= 100,000 | 52/52 | 0.83 | >= 1 | 83% | 83% (43/52) | 83% | 83% | 9 |
| Combo: article AND artist article | 52/52 | 0.80 | >= 1 | 80% | 81% (42/52) | 81% | 81% | 10 |
| Combo: article AND LB listens >= 25,000 | 52/52 | 0.82 | >= 1 | 82% | 83% (43/52) | 83% | 83% | 9 |
| Combo: Deezer rank >= 400k AND (article OR LB>=25k) | 41/52 | 0.75 | >= 1 | 75% | 76% (31/41) | 76% | 77% | 10 |

### Misclassified songs at each best cut

**Deezer track rank**, cut >= 430,856: 10 wrong of 41.
- Called KNOWN but are NOT (5): bomfunk mc's - freestyler (NOT, 625,708); clozee, barbatuques - baiana (clozee remix) (NOT, 458,615); nimino, manta - better (NOT, 519,445); aespa - Spicy (NOT, 538,392); Busy P - Genie (NOT, 443,090)
- Called NOT but are KNOWN (5): belinda carlisle - heaven is a place on e... (KNOWN, 247,477); kreayshawn - bumpin bumpin (KNOWN, 184,098); kreayshawn - go hard (la.la.la) (KNOWN, 336,506); kreayshawn - gucci gucci (KNOWN, 389,480); run the jewels - ooh la la (feat. greg ni... (KNOWN, 251,350)

**Deezer artist fans**, cut >= 39,055: 15 wrong of 50.
- Called KNOWN but are NOT (7): andrew dum, alina eremia, café del mundo ... (NOT, 81,031); basement jaxx - never say never (extended... (NOT, 93,118); basement jaxx, lisa kekaula - good luck (NOT, 93,118); flume - what you need (NOT, 376,436); aespa - Spicy (NOT, 471,157); Busy P - Genie (NOT, 67,407); Disclosure - Waterfall (NOT, 987,727)
- Called NOT but are KNOWN (8): biz markie - just a friend (KNOWN, 12,167); black box - everybody everybody (KNOWN, 20,620); bow wow wow - i want candy (KNOWN, 4,185); craig mack - flava in ya ear (KNOWN, 7,533); crystal waters - gypsy woman (she's homel... (KNOWN, 18,731); kreayshawn - bumpin bumpin (KNOWN, 26,618); kreayshawn - go hard (la.la.la) (KNOWN, 26,618); kreayshawn - gucci gucci (KNOWN, 26,618)

**ListenBrainz listens**, cut >= 26,008: 7 wrong of 46.
- Called KNOWN but are NOT (4): basement jaxx, lisa kekaula - good luck (NOT, 257,059); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 87,636); bomfunk mc's - freestyler (NOT, 360,136); aespa - Spicy (NOT, 31,061)
- Called NOT but are KNOWN (3): 2 unlimited - get ready (KNOWN, 2,968); kreayshawn - bumpin bumpin (KNOWN, 1,113); Disclosure - NO CAP (KNOWN, 1,597)

**ListenBrainz users**, cut >= 3,481: 6 wrong of 46.
- Called KNOWN but are NOT (3): basement jaxx, lisa kekaula - good luck (NOT, 39,037); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 8,505); bomfunk mc's - freestyler (NOT, 48,401)
- Called NOT but are KNOWN (3): 2 unlimited - get ready (KNOWN, 854); kreayshawn - bumpin bumpin (KNOWN, 234); Disclosure - NO CAP (KNOWN, 559)

**Wikipedia song pageviews 12 mo (0 if no article)**, cut >= 20,461: 8 wrong of 52.
- Called KNOWN but are NOT (1): bomfunk mc's - freestyler (NOT, 76,640)
- Called NOT but are KNOWN (7): 2 unlimited - get ready (KNOWN, 0); death grips - takyon (death yon) (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); kreayshawn - go hard (la.la.la) (KNOWN, 2,034); run the jewels - legend has it (KNOWN, 3,739); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Wikipedia artist pageviews 12 mo**, cut >= 87,588: 5 wrong of 40.
- Called KNOWN but are NOT (5): basement jaxx - never say never (extended... (NOT, 114,612); basement jaxx, lisa kekaula - good luck (NOT, 114,612); flume - what you need (NOT, 106,271); aespa - Spicy (NOT, 589,195); Disclosure - Waterfall (NOT, 117,497)
- Called NOT but are KNOWN (0): none

**Song has Wikipedia article (1/0)**, cut >= 1: 10 wrong of 52.
- Called KNOWN but are NOT (5): basement jaxx - never say never (extended... (NOT, 1); basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (5): 2 unlimited - get ready (KNOWN, 0); death grips - takyon (death yon) (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Artist has Wikipedia article (1/0)**, cut >= 1: 18 wrong of 52.
- Called KNOWN but are NOT (18): basement jaxx - never say never (extended... (NOT, 1); basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); clozee, barbatuques - baiana (clozee remix) (NOT, 1); flume - what you need (NOT, 1); fox stevenson - all eyes on me (NOT, 1); fox stevenson - bring a coat (NOT, 1); fox stevenson - don't care crown (NOT, 1); fox stevenson - give me some space (NOT, 1); fox stevenson - glue gun (NOT, 1); fox stevenson - human in the evening (NOT, 1); fox stevenson - road to nothing (NOT, 1); fox stevenson - that choice (NOT, 1); nimino - orla (NOT, 1); nimino, manta - better (NOT, 1); aespa - Spicy (NOT, 1); Disclosure - Waterfall (NOT, 1)
- Called NOT but are KNOWN (0): none

**Song article mentions a national chart (1/0)**, cut >= 1: 11 wrong of 52.
- Called KNOWN but are NOT (4): basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (7): 2 unlimited - get ready (KNOWN, 0); death grips - takyon (death yon) (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); kreayshawn - go hard (la.la.la) (KNOWN, 0); run the jewels - legend has it (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Combo: article OR LB listens >= 100,000**, cut >= 1: 9 wrong of 52.
- Called KNOWN but are NOT (5): basement jaxx - never say never (extended... (NOT, 1); basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (4): 2 unlimited - get ready (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Combo: article AND artist article**, cut >= 1: 10 wrong of 52.
- Called KNOWN but are NOT (5): basement jaxx - never say never (extended... (NOT, 1); basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (5): 2 unlimited - get ready (KNOWN, 0); death grips - takyon (death yon) (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Combo: article AND LB listens >= 25,000**, cut >= 1: 9 wrong of 52.
- Called KNOWN but are NOT (4): basement jaxx, lisa kekaula - good luck (NOT, 1); bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (5): 2 unlimited - get ready (KNOWN, 0); death grips - takyon (death yon) (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

**Combo: Deezer rank >= 400k AND (article OR LB>=25k)**, cut >= 1: 10 wrong of 41.
- Called KNOWN but are NOT (3): bodyrox, luciana - yeah yeah (d ramirez v... (NOT, 1); bomfunk mc's - freestyler (NOT, 1); aespa - Spicy (NOT, 1)
- Called NOT but are KNOWN (7): 2 unlimited - get ready (KNOWN, 0); belinda carlisle - heaven is a place on e... (KNOWN, 0); kreayshawn - bumpin bumpin (KNOWN, 0); kreayshawn - go hard (la.la.la) (KNOWN, 0); kreayshawn - gucci gucci (KNOWN, 0); run the jewels - ooh la la (feat. greg ni... (KNOWN, 0); Disclosure - NO CAP (KNOWN, 0)

## The two-vote rule on the same songs

Added the same day, for [ADR 0089](../adr/0089-familiarity-is-read-off-the-songs-own-listeners-and-its-wikipedia-article.md):
ListenBrainz users ≥ 3,481 is one vote, the song's own article ≥ 20,461 pageviews a year (0 without
one) is the other; both known reads `Mainstream`, neither `Obscure`, a split `Niche`. Over the 52:

| label | Mainstream | Niche | Obscure |
|---|---|---|---|
| KNOWN (22) | 15 | 4 | 3 |
| NOT (30) | 1 | 2 | 27 |

On the wrong extreme: *Freestyler* (NOT, read Mainstream); *Get Ready*, *Bumpin Bumpin* and *NO CAP*
(KNOWN, read Obscure). Niche: *Good Luck* and *Yeah Yeah* (NOT); *Takyon*, *Go Hard*, *Legend Has
It* and *Ooh La La* (KNOWN). The cuts were fitted on these songs, so this is optimistic.
