# Retyping warps in the library: what the rule does to 519 songs

*Note, 2026-10-13, frozen. For #99 and #96; decides nothing.*

A dry run of `WarpTrueUp.Plan` over every song in the Library and Staging roots on 2026-10-13
(`TrueUpDecision`, thresholds below). Nothing was written.

## The rule as built

For a steady warp with no step:

1. **A whole number** when the nearest one fits Live's transients at least as well as the warp's
   tempo, would save 2 ms or more over the track, and leaves no drift worth believing.
2. **Otherwise the tempo that removes the measured drift**, when the drift adds up to 5 ms or more
   over the track and stands 3 standard errors from nothing. The drift is the slope of the 10 s
   windows' offsets against the warp (`GridCheck`, the stronger half of the transients); the tempo is
   `bpm / (1 + drift / 60000)` with the drift in ms per minute. It is not an onset fit, so the
   decision does not depend on the analysis it is judging.
3. Either way the retyped grid is re-checked: no step, no more drift and no wider spread than
   before, and a drift under 3 standard errors (never fewer than 0.1 ms per minute).

Thresholds come from the library: window offsets scatter by about 2 ms, so a drift measured over
20 windows of a 3.5 minute track has a standard error of about 0.5 ms per minute, 1.8 ms over the
track. 5 ms is about 2.8 of those; the 3-error test is the real gate and 5 ms the floor under it.

## What the library holds

| outcome | songs |
|---|---|
| left: holds within the noise | 323 |
| left: the warp changes tempo | 66 |
| left: a step | 25 |
| left: no canonical stem has a warp | 23 |
| left: a tempo that removes the drift does not hold | 2 |
| left: too few transients | 1 |
| left: canonical stems disagree | 1 |
| **retyped to a whole number** | **33** |
| **retyped to a drift-corrected tempo** | **45** |

- **The premises in #99 moved.** Pharrell Williams – *Happy* and Taylor Swift – *Shake It Off* both
  read 160.0000 now and are left; they were retyped by hand after the issue was written. The
  Notorious B.I.G. – *Gin and Juice* (94.6174, whole 95 fits 0.0394 against 0.8891) is left, as the
  issue says. Too $hort – *Blow the Whistle* (99.985) is left: 100 fits 0.8430 against 0.8999, and
  its drift is 2.4 ms over the track.
- **Half of the drift retypes are not near a whole number.** 23 of the 45 land more than 0.05 from
  one (Ini Kamoze – *Here Comes the Hotstepper* 100.3485 → 100.3362, Rob Zombie – *Dragula* 125.0042
  → 125.0257), so an integer-only rule would have left them drifting. Ten of the 45 start from a whole number the user
  typed (Awich – *Queendom* 105 → 105.0103): the track is not at it.
- **After the retype** the largest remaining drift among the whole-number retypes is 6.3 ms over
  the track, under 3 standard errors; among the drift retypes it is 0.3 ms.
- No copies crateside lent from these warps are recorded on this machine, so nothing is re-lent in
  the dry run.

## The songs

### Retyped to a whole number

| song | warp | new | drift over the track, ms | after |
|---|---|---|---|---|
| Atura - Think About You | 139.9972 | 140.0000 | -5.9 | -2.9 |
| BONESAW - HOT SENS4TION | 155.0043 | 155.0000 | +4.4 | -1.8 |
| Bloodhound Gang - The Bad Touch | 123.0015 | 123.0000 | +3.7 | +0.6 |
| Calvin Harris, Jessie Reyez - Ocean | 136.0020 | 136.0000 | +3.3 | +0.1 |
| Chamillionaire - Ridin | 143.0032 | 143.0000 | +6.4 | -0.3 |
| Flo Rida - Low (feat. T-Pain) | 128.0020 | 128.0000 | +7.2 | +3.6 |
| Flume - What You Need | 150.0043 | 150.0000 | +11.1 | +4.0 |
| Fox Stevenson - Road To Nothing | 149.9952 | 150.0000 | -11.4 | -4.8 |
| Grabbitz - Better Off Before | 149.9964 | 150.0000 | -3.6 | +0.8 |
| Gwen Stefani - Hollaback Girl | 110.0016 | 110.0000 | +3.6 | +0.7 |
| Houshou Marine - A Horny Money World ～伝説の夜～ | 145.0036 | 145.0000 | +4.7 | -0.1 |
| Hurricane Chris - A Bay Bay | 76.9962 | 77.0000 | -13.9 | +1.3 |
| Kesha - TiK ToK | 120.0034 | 120.0000 | +4.0 | -1.6 |
| Kirara Magic & Yuuna Nini - Cozy | 90.0016 | 90.0000 | +5.0 | +1.4 |
| Kreayshawn - Gucci Gucci | 155.0167 | 155.0000 | +17.0 | -3.0 |
| La Roux - Tigerlily | 141.9928 | 142.0000 | -8.3 | +2.0 |
| Lemon Demon - Geocities | 144.9912 | 145.0000 | -10.2 | +2.3 |
| Ludacris, Pharrell - Money Maker | 84.0075 | 84.0000 | +21.8 | +1.1 |
| Macklemore & Ryan Lewis - Thrift Shop | 94.9956 | 95.0000 | -17.1 | -6.3 |
| Magnificence, 7 SKIES - The Drill | 125.0048 | 125.0000 | +8.0 | -0.3 |
| Mark Breeze - Be Alone | 169.9962 | 170.0000 | -5.9 | -0.3 |
| Moe Shop - Cerise | 170.0031 | 170.0000 | +4.4 | +0.3 |
| N.E.R.D. - Lapdance | 101.9927 | 102.0000 | -20.5 | -5.4 |
| NANAOAKARI - Kanzen Houki Sengen | 190.0016 | 190.0000 | +2.7 | +0.4 |
| Nova Rockafeller - Heart Beat ft. Ollie G, Granny + Zolo | 120.0045 | 120.0000 | +5.3 | -0.3 |
| PSY - DADDY (feat. CL of 2NE1) | 132.0022 | 132.0000 | +3.1 | -0.8 |
| Sarah Cothran - As the World Caves In | 124.0049 | 124.0000 | +7.8 | +1.6 |
| Tame Impala - Dracula | 115.0049 | 115.0000 | +15.0 | +6.2 |
| Taylor Swift - Wi$h Li$t | 73.9979 | 74.0000 | -8.1 | -2.2 |
| Tiësto, KAMILLE - What’s It Gonna Be | 128.0039 | 128.0000 | +5.4 | +0.6 |
| aespa - Spicy | 115.0016 | 115.0000 | +1.6 | -1.2 |
| tn-shi - Catch me if you can | 174.0023 | 174.0000 | +5.5 | +2.2 |
| tonytonychopper999 - ENERGIZE | 159.9891 | 160.0000 | -11.7 | +1.0 |

### Retyped to the tempo that removes the drift

| song | warp | new | drift over the track, ms | after |
|---|---|---|---|---|
| Amerie - 1 Thing | 100.0086 | 100.0040 | +11.1 | +0.1 |
| Awich - Queendom | 105.0000 | 105.0103 | -20.3 | -0.3 |
| Biz Markie - Just a Friend | 85.2813 | 85.2752 | +17.1 | +0.1 |
| Black Box - Everybody Everybody | 117.5724 | 117.5799 | -15.6 | -0.1 |
| Bomfunk MC's - Freestyler | 163.8435 | 163.8471 | -6.7 | -0.0 |
| Busy P - Genie (feat. Mayer Hawthorne) | 104.0000 | 103.9979 | +5.4 | +0.0 |
| DMX - Where The Hood At | 92.9966 | 93.0042 | -23.4 | -0.0 |
| DMX, Swizz Beatz - Get It On The Floor | 94.2046 | 94.1988 | +16.0 | +0.0 |
| David Banner, Twista, Busta Rhymes - Like A Pimp (Remix) | 144.1373 | 144.1426 | -8.7 | -0.0 |
| Dr. Dre, Eminem - Forgot About Dre | 133.8768 | 133.8706 | +10.4 | +0.1 |
| Eason Chan - 这样很好 (Isha's Song) | 75.0000 | 75.0056 | -19.9 | -0.1 |
| Eminem - My Name Is | 85.5183 | 85.5165 | +5.8 | -0.0 |
| Eminem - Without Me | 112.2503 | 112.2529 | -6.5 | -0.0 |
| Enur, Natasja - Calabria 2008 | 126.0000 | 126.0072 | -13.3 | +0.0 |
| Genesis - Invisible Touch | 131.0358 | 131.0440 | -12.8 | -0.0 |
| Ini Kamoze - Here Comes the Hotstepper (Heartical Mix) | 100.3485 | 100.3362 | +30.7 | +0.2 |
| Lil Wayne, Bobby V., Kidd Kidd - Mrs. Officer | 84.0000 | 83.9979 | +7.1 | +0.0 |
| Ludacris - Get Back | 86.0000 | 85.9976 | +7.5 | +0.0 |
| Ludacris - Rollout (My Business) | 131.0563 | 131.0495 | +15.4 | +0.0 |
| Ludacris, Mystikal, I-20 - Move Bitch | 177.8599 | 177.8516 | +12.5 | -0.0 |
| Ludacris, Nate Dogg - Area Codes | 100.9901 | 100.9946 | -13.7 | -0.0 |
| MANUEL - GAS GAS GAS | 154.0000 | 154.0033 | -5.6 | -0.0 |
| Mariah Carey - Fantasy | 102.3716 | 102.3678 | +9.0 | +0.0 |
| Mark Morrison - Return of the Mack | 95.4295 | 95.4331 | -8.2 | +0.0 |
| Marky Mark And The Funky Bunch - Good Vibrations | 121.6353 | 121.6317 | +8.0 | +0.0 |
| Montell Jordan - This Is How We Do It | 103.6414 | 103.6438 | -6.4 | -0.0 |
| Muse - Hysteria | 93.0036 | 93.0079 | -10.7 | -0.0 |
| NEUN - About the Subject | 110.0000 | 109.9960 | +7.3 | +0.0 |
| Nine Inch Nails - Into The Void | 93.0252 | 93.0207 | +13.8 | +0.1 |
| Nine Inch Nails - Zero Sum | 148.0000 | 147.9976 | +6.0 | +0.0 |
| Ol Dirty Bastard - Shimmy Shimmy Ya | 96.2303 | 96.2261 | +7.1 | +0.0 |
| Pet Shop Boys - West End Girls (2001 Remaster) | 113.2361 | 113.2387 | -6.4 | -0.0 |
| Phantoms, Hayley Kiyoko - Need You Closer | 126.0034 | 125.9997 | +7.6 | +0.0 |
| Princess Superstar - Perfect | 119.5090 | 119.5018 | +12.5 | -0.0 |
| ROUND TABLE featuring Nino - Let Me Be With You | 130.0044 | 130.0012 | +6.7 | +0.0 |
| Radiohead - Talk Show Host | 88.8323 | 88.8278 | +14.1 | +0.0 |
| Rob Zombie - Dragula | 125.0042 | 125.0257 | -38.3 | -0.1 |
| Roxette - It Must Have Been Love | 80.6465 | 80.6441 | +7.7 | +0.0 |
| SNAP! - The Power | 108.8899 | 108.8941 | -8.7 | -0.1 |
| Souls Of Mischief - 93 'Til Infinity | 103.0670 | 103.0645 | +6.8 | +0.0 |
| Talk Talk - It's My Life | 126.3078 | 126.3133 | -10.3 | -0.0 |
| The Game, 50 Cent - Hate It Or Love It | 100.0100 | 100.0129 | -5.9 | -0.0 |
| The Naked And Famous - Young Blood | 105.0000 | 104.9978 | +5.2 | +0.0 |
| The Notorious B.I.G. - Big Poppa | 84.5212 | 84.5241 | -8.7 | -0.0 |
| The Prodigy - Voodoo People | 148.9950 | 148.9930 | +5.3 | +0.0 |

## With the grid moved onto the transients (#96)

The same 78 retypes with `rephase`: the retyped warp is moved so the kept drum stem's strongest
transients (the instrumental's where none is kept; 4 of the 78 have a drum stem) center on its grid,
and kept only where every 30 s then sits within 5 ms of it.

| outcome | songs |
|---|---|
| moved and kept | 63 |
| grid would not hold within 5 ms all through | 15 |

The moves are 3.0 ms at the median and 16.3 ms at most.
