# Changelog

## [1.9.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.8.0...v1.9.0) (2026-10-09)


### Features

* **app:** the controls editor binds keys to the right stick ([0516e28](https://github.com/enriquezchristopher/CouchLink/commit/0516e2802b468dd1e7af3ee224de7517f7da683b))
* **app:** the NBA 2K22 profile puts the pro stick on Num 8/2/4/6 ([0a5484d](https://github.com/enriquezchristopher/CouchLink/commit/0a5484d9498640f3a7cef99e5fc9324d7357a402))
* bind keys to the right stick, alongside the mouse ([563ba75](https://github.com/enriquezchristopher/CouchLink/commit/563ba7548aebef155d7c0547f71cc42d83f238d4))
* **input:** held right-stick keys set the stick, the mouse takes over when they're released ([392e540](https://github.com/enriquezchristopher/CouchLink/commit/392e540150160739e754b0cb475a1686f09123d2))
* **input:** right-stick directions as bindable controls; F1 lists them once one is bound ([a5d774a](https://github.com/enriquezchristopher/CouchLink/commit/a5d774a517784974aa8e79d6d88f5c4a10ffd9e9))


### Bug Fixes

* **input:** mouse movement while a right-stick key is held can't slip through on release ([734e7db](https://github.com/enriquezchristopher/CouchLink/commit/734e7db34f38eb870d4d07f9fcddef5607553dab))
* **video:** overlay text taller than the player window shrinks to fit ([f86b450](https://github.com/enriquezchristopher/CouchLink/commit/f86b450e3ba913c845e69bf016933b52bc2a6b31))

## [1.8.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.7.0...v1.8.0) (2026-10-09)


### Features

* **app:** stream quality setting in the host lobby, with a network link warning ([8f9534e](https://github.com/enriquezchristopher/CouchLink/commit/8f9534e864d10a21d294b874f6d2ee3f9f303007))
* **core:** slower NVENC and AMF settings for high stream quality ([8f9534e](https://github.com/enriquezchristopher/CouchLink/commit/8f9534e864d10a21d294b874f6d2ee3f9f303007))
* **core:** stream quality presets scale the bitrate up to 100 Mbps ([8f9534e](https://github.com/enriquezchristopher/CouchLink/commit/8f9534e864d10a21d294b874f6d2ee3f9f303007))
* **core:** warn when the stream would outgrow the host's network link ([8f9534e](https://github.com/enriquezchristopher/CouchLink/commit/8f9534e864d10a21d294b874f6d2ee3f9f303007))


### Bug Fixes

* **video:** turn off driver auto-processing in the host converter ([8f9534e](https://github.com/enriquezchristopher/CouchLink/commit/8f9534e864d10a21d294b874f6d2ee3f9f303007))

## [1.7.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.6.2...v1.7.0) (2026-10-08)


### Features

* **app:** load, browse and save controller profiles in the controls editor ([8a67fe1](https://github.com/enriquezchristopher/CouchLink/commit/8a67fe16feab7eadfac5a7af3986cbe81f07c015)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **app:** ship an NBA 2K22 profile with the game's own keyboard keys ([c037440](https://github.com/enriquezchristopher/CouchLink/commit/c037440e2f14969a23a0cda33c01d50e594be0a4)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* controller profiles, with an NBA 2K22 profile ([eb17ec0](https://github.com/enriquezchristopher/CouchLink/commit/eb17ec06e91d5badbc440c59b6aa7f6abaec7ef3))
* **input:** fixed key IDs for controller profile files ([347ffe2](https://github.com/enriquezchristopher/CouchLink/commit/347ffe21c700902a186365dafbcf64735ed871f6)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **input:** list, read and safely write profile files ([b29ba88](https://github.com/enriquezchristopher/CouchLink/commit/b29ba88408c79980d4d4e71dfb36dcb4cf5e8700)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **input:** load a profile into the controls, with action labels ([ed757ed](https://github.com/enriquezchristopher/CouchLink/commit/ed757ede5ac061df5a785eb794091f68f951b19c)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **input:** read and write controller profile files ([30f10dd](https://github.com/enriquezchristopher/CouchLink/commit/30f10dd06da3e593dfece7a648e44fe48060067e)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **input:** the F1 panel shows the profile name and action labels ([b71c1af](https://github.com/enriquezchristopher/CouchLink/commit/b71c1af856988ede881cf56673c36240ee5ba51b)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)


### Bug Fixes

* **app:** Show labels reacts to Checked/Unchecked, so accessibility tools can tick it ([ac5d705](https://github.com/enriquezchristopher/CouchLink/commit/ac5d705e14dcc0445479456f7cd9648ae08edd8e)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)
* **input:** a profile with half an emoji escape is refused instead of crashing ([f2d55cd](https://github.com/enriquezchristopher/CouchLink/commit/f2d55cd63c3b1fa9e2a2de5eed4d7fa8ebd85cbd)), closes [#56](https://github.com/enriquezchristopher/CouchLink/issues/56)

## [1.6.2](https://github.com/enriquezchristopher/CouchLink/compare/v1.6.1...v1.6.2) (2026-10-08)


### Performance

* **video:** the host's capture and encode get GPU priority over the game ([96efed8](https://github.com/enriquezchristopher/CouchLink/commit/96efed88ddf7207b9378f37b8060af111cd086c0))
* **video:** the host's capture and encode get GPU priority over the game ([79e0c5a](https://github.com/enriquezchristopher/CouchLink/commit/79e0c5a191b38b34c1c77b57520deb280d4804f8))

## [1.6.1](https://github.com/enriquezchristopher/CouchLink/compare/v1.6.0...v1.6.1) (2026-10-08)


### Bug Fixes

* **video:** older AMD GPUs encode with AMF lowlatency instead of falling back to the CPU ([efef461](https://github.com/enriquezchristopher/CouchLink/commit/efef461bc3de5117f0934eae79c01450830acd23))
* **video:** older AMD GPUs encode with AMF lowlatency instead of falling back to the CPU ([e6cd213](https://github.com/enriquezchristopher/CouchLink/commit/e6cd213d2523e4977f28665b8f1101a84387ab89))

## [1.6.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.5.0...v1.6.0) (2026-10-08)


### Features

* **app:** controls editor on the Start and session screens and over the game; side mouse buttons ([27ec88f](https://github.com/enriquezchristopher/CouchLink/commit/27ec88fa41072ea050ba80aaf6b7610564d7ca12))
* **app:** launching again brings the running copy forward ([142a2ab](https://github.com/enriquezchristopher/CouchLink/commit/142a2ab6593a2cd8aed7fbfc88e185a6de5c8f90))
* **core:** editable key layout with one key per control, reserved keys and key names ([8841658](https://github.com/enriquezchristopher/CouchLink/commit/88416589e1d127d4a071fddd4dc24daf44fe789e))
* **core:** in-memory control settings with sensitivity steps and Invert Y; the mapper follows edits ([d625ed9](https://github.com/enriquezchristopher/CouchLink/commit/d625ed98c8731c5fb7ce5e9c4982feb578d100a2))
* **core:** which Windows shortcuts to block while playing, and the single-instance decision ([314fbe4](https://github.com/enriquezchristopher/CouchLink/commit/314fbe474e632b6fc9a4e16d7788c996d99c6a32))
* playing screen, controls editor and single instance (Plan 8) ([78869aa](https://github.com/enriquezchristopher/CouchLink/commit/78869aa5c29c4b9cef317e73df952b26bb6faa97))
* **video:** F1 controls panel and a 5 s start hint; stats move to the top-right ([09b9d39](https://github.com/enriquezchristopher/CouchLink/commit/09b9d396e66ad5b02d2589bbc3887ced4bb9d931))
* **video:** the fullscreen player blocks Windows shortcuts and keeps the pointer; F1 and Ctrl+Alt+C ([ffd457f](https://github.com/enriquezchristopher/CouchLink/commit/ffd457f20a08f4d4c5ee9f69297d26e4e6eb0efd))


### Bug Fixes

* **video:** the keyboard hook gets its own thread so a busy player can't lag keys or lose the hook ([155efd0](https://github.com/enriquezchristopher/CouchLink/commit/155efd06c352226bdc671d2d0304535e3555439a))

## [1.5.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.4.0...v1.5.0) (2026-10-08)


### Features

* **app:** join list, waiting and playing screens; leave, kick and reconnect end in clear messages ([d1e66d0](https://github.com/enriquezchristopher/CouchLink/commit/d1e66d0b295af5bd182c69e46fdb5d28b3a7d756))
* **app:** Start screen, host lobby with players, kick and stream settings, approval popup ([79df969](https://github.com/enriquezchristopher/CouchLink/commit/79df9699ad8e6d5f372c35956e67d56e88529801))
* **core:** client session: join, wait, play, reconnect for 10 s, and every way it ends ([7f4c082](https://github.com/enriquezchristopher/CouchLink/commit/7f4c082fba0d328198a6d46ccbe44f2f5dd846d1))
* **core:** host announce packet (wire type 7), PC names and the discovery and session ports ([fe0f478](https://github.com/enriquezchristopher/CouchLink/commit/fe0f4780279208c1d7f0a16f1c566108f7bc485b))
* **core:** host session: approval, slots, heartbeat silence, 60 s reservations, kick and stop ([f904e57](https://github.com/enriquezchristopher/CouchLink/commit/f904e57bfbb1d81217c2ff86134446a27114cbcb))
* **core:** LAN discovery: host announces on every adapter, clients keep a host list ([44c850f](https://github.com/enriquezchristopher/CouchLink/commit/44c850fe75b57ec31f0c6affe81a13ffdab95e25))
* **core:** pads are plugged, held and unplugged explicitly and bound to the client's address ([1c527b9](https://github.com/enriquezchristopher/CouchLink/commit/1c527b93143949b788c0ab0455498b80e2e87d29))
* **core:** session messages (wire types 8-14) and length-prefixed framing ([bc2771a](https://github.com/enriquezchristopher/CouchLink/commit/bc2771abd39ab4c144fa73beb30fb87f870b0d68))
* **core:** session server and client over TCP 47801 with heartbeats ([f7d28d0](https://github.com/enriquezchristopher/CouchLink/commit/f7d28d0db0a5ad057356c8bb114fbe7fba67bc44))
* **core:** video and audio targets are set explicitly, not learned from input ([791fa60](https://github.com/enriquezchristopher/CouchLink/commit/791fa60afb18253b99cc9fa68ae69a6e5c56d721))
* find the host in a list and join with approval (Plan 7) ([519072b](https://github.com/enriquezchristopher/CouchLink/commit/519072bef4c4ecd802e71ebcc562caad8f944b50))
* **video:** the player shows the session's Reconnecting line over the last picture ([7d104f4](https://github.com/enriquezchristopher/CouchLink/commit/7d104f433bb8dcbc8e1f3d6328203862e4579e3c))

## [1.4.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.3.0...v1.4.0) (2026-10-07)


### Features

* **app:** host streams its sound and clients play it, muted when the host is the same PC ([7c88a79](https://github.com/enriquezchristopher/CouchLink/commit/7c88a79678b808e25737ddbc1fd247830735e9d7))
* **audio:** CouchLink.Audio project with the Opus encoder and decoder ([f033de1](https://github.com/enriquezchristopher/CouchLink/commit/f033de10af4813d4c33e70b508d738866cd81b52))
* **audio:** WASAPI loopback capture and low-latency default-device output ([3650e33](https://github.com/enriquezchristopher/CouchLink/commit/3650e33437981945d9634468db21bab39065ebe9))
* clients hear the host's sound (Plan 6) ([2030e4d](https://github.com/enriquezchristopher/CouchLink/commit/2030e4d87a5af890789728e06191e566bc07c573))
* **core:** audio drift control ([68e7eb3](https://github.com/enriquezchristopher/CouchLink/commit/68e7eb30d9d761074933e57694e50e2c5d60daeb))
* **core:** audio jitter buffer with redundancy, concealment and priming ([1f00db9](https://github.com/enriquezchristopher/CouchLink/commit/1f00db9f8c05c0fc6b6148db752d53619149f275))
* **core:** audio packet (wire type 6) and audio format constants ([a9958ac](https://github.com/enriquezchristopher/CouchLink/commit/a9958ac03c814f978972dfc3ed846027bbc043d5))
* **core:** audio source interface, frame slicer and test tone ([1f03402](https://github.com/enriquezchristopher/CouchLink/commit/1f0340221e9174c15b0ec754efaafeaf4f74f840))
* **core:** client audio with decode, concealment and drift correction ([c37c824](https://github.com/enriquezchristopher/CouchLink/commit/c37c824f7a0d6b3848b8468762e095806a8afb7d))
* **core:** host audio streamer with redundancy and discontinuity handling ([d731cfb](https://github.com/enriquezchristopher/CouchLink/commit/d731cfbe66ede28097cc88e94e25858689d4917f))
* **core:** share the client's video port with audio; same-PC check ([fe27ad1](https://github.com/enriquezchristopher/CouchLink/commit/fe27ad1cf59344d1696a9c625ba966a68b3d23dc))
* **video:** audio line in the F2 overlay; --test-tone and --audio-loss switches ([6075fe2](https://github.com/enriquezchristopher/CouchLink/commit/6075fe2922be81aa8dbd06f13ffba734b9bb5c00))


### Bug Fixes

* **audio:** one reopen at a time, no exceptions on timer threads, audio never fails a join ([917468f](https://github.com/enriquezchristopher/CouchLink/commit/917468fe6961be752ac09d9b2f3c07f8c93ca641))
* **core:** cap the drift target at 20 ms so a startup burst isn't kept as latency ([10b85a1](https://github.com/enriquezchristopher/CouchLink/commit/10b85a1890e70fa9bc4bedfa2f0248526ed17805))

## [1.3.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.2.0...v1.3.0) (2026-10-06)


### Features

* **app:** joining opens the host's screen fullscreen; Ctrl+Alt+Q leaves ([24dcd37](https://github.com/enriquezchristopher/CouchLink/commit/24dcd3765f5276f9ea8b1a63fa2edd4c476bccd6)), closes [#17](https://github.com/enriquezchristopher/CouchLink/issues/17)
* clients see the host's screen, with an F2 stats overlay (Plan 5) ([630668e](https://github.com/enriquezchristopher/CouchLink/commit/630668e3efa3995969b32f01a750062164ddd13c))
* **core:** client measures round trip and resyncs after a decode error ([e4b20f9](https://github.com/enriquezchristopher/CouchLink/commit/e4b20f93edde43b1e22b1bcfc4e3e708d67ef75b))
* **core:** host measures capture-to-send delay and answers timing pings ([a830eb7](https://github.com/enriquezchristopher/CouchLink/commit/a830eb7b4a866a9c599b89c131d56da3b9382b8f)), closes [#18](https://github.com/enriquezchristopher/CouchLink/issues/18)
* **core:** letterbox, per-second stats and overlay text for the player ([2a1d3f9](https://github.com/enriquezchristopher/CouchLink/commit/2a1d3f96922c45d28db164f03d872afaf00579d8)), closes [#18](https://github.com/enriquezchristopher/CouchLink/issues/18)
* **core:** timing ping and reply packets ([5f3a82b](https://github.com/enriquezchristopher/CouchLink/commit/5f3a82bf04e95c18c4d52b964891e9f02afa0e39)), closes [#18](https://github.com/enriquezchristopher/CouchLink/issues/18)
* **video:** fullscreen player window with D3D11 presentation and overlay ([d3d1e89](https://github.com/enriquezchristopher/CouchLink/commit/d3d1e897ad7ccf2698b802f3e47e84ff1c58dafc))
* **video:** H.264 decoder with D3D11VA and software fallback ([7761dcc](https://github.com/enriquezchristopher/CouchLink/commit/7761dccf8c3e1c1fddbc1cd7f2794572bc436edc)), closes [#17](https://github.com/enriquezchristopher/CouchLink/issues/17)
* **video:** player logic with software fallback, resync and status text ([cb39d2d](https://github.com/enriquezchristopher/CouchLink/commit/cb39d2dac55f2fb014d6e5e2c909e69fac7ed230))


### Bug Fixes

* **video:** keep the last picture, say when the host is quiet, contain window errors ([49832e0](https://github.com/enriquezchristopher/CouchLink/commit/49832e0fd8038987f05be5cc8f289e10a4449317))

## [1.2.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.1.1...v1.2.0) (2026-10-06)


### Features

* **app:** host shares its screen at the chosen resolution and frame rate ([3037a01](https://github.com/enriquezchristopher/CouchLink/commit/3037a01effa2dcab7d2d3995df97e93069d4e010)), closes [#14](https://github.com/enriquezchristopher/CouchLink/issues/14)
* **app:** stream a test pattern from host to clients ([640a21c](https://github.com/enriquezchristopher/CouchLink/commit/640a21c0c47b5ffdd828077c87968e3f6456702d)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** choose the H.264 encoder by GPU vendor with low-latency options ([db5df97](https://github.com/enriquezchristopher/CouchLink/commit/db5df97fb1211ca01a993b516dac98147776c591))
* **core:** drop undecodable frames and request keyframes after loss ([8c4e377](https://github.com/enriquezchristopher/CouchLink/commit/8c4e377936c257e522279d0ae096fe735f205714)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** encoded frame source interface and test pattern ([56d58a9](https://github.com/enriquezchristopher/CouchLink/commit/56d58a9eed99ec964ed252c454081abf2acc6efe)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** host keyframe pacing and stream targets ([ef3830e](https://github.com/enriquezchristopher/CouchLink/commit/ef3830ed057665e81c8ec1a59b5126dc4ec87811)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** host stream settings for resolution and frame rate ([9e95a97](https://github.com/enriquezchristopher/CouchLink/commit/9e95a97f75d44a82f1d02207559c5b41dc9f331d))
* **core:** host video streamer and client video pipeline ([bb272a1](https://github.com/enriquezchristopher/CouchLink/commit/bb272a13d84aeef714c198d71e94f94de83f70cd)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** mark frames sent while the host's capture is lost ([45d045b](https://github.com/enriquezchristopher/CouchLink/commit/45d045b49482cff1fb8084d60d1cfdbbb0a0733c))
* **core:** reassemble and repair frames from shard packets ([1d98348](https://github.com/enriquezchristopher/CouchLink/commit/1d9834822b36432d30faa2533878b2693b54dbce)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** Reed-Solomon erasure code over GF(256) ([8677410](https://github.com/enriquezchristopher/CouchLink/commit/8677410776f7687124648db5d6e0b7d932012876)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** split encoded frames into FEC-protected shard packets ([99e6457](https://github.com/enriquezchristopher/CouchLink/commit/99e6457fe501511b7eaaad5aa9a9b7c077f2cb2e)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** UDP transport for video and keyframe requests ([15c8803](https://github.com/enriquezchristopher/CouchLink/commit/15c8803fa47c4f644a347509d845009aef99beca)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** video shard and keyframe request packets ([bc1dad5](https://github.com/enriquezchristopher/CouchLink/commit/bc1dad5537237f5545c11ca6889531f384fa4546)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **video:** desktop duplication capture and VideoTest capture check ([c645fc0](https://github.com/enriquezchristopher/CouchLink/commit/c645fc0a8e755d1f6719dbf36a1258a1b4e8a449))
* **video:** GPU NV12 conversion and H.264 encoding through FFmpeg ([affabe4](https://github.com/enriquezchristopher/CouchLink/commit/affabe41fed0d1356842ff7e68b6d283cf617568))
* **video:** screen source with pacing, repeats, pauses and encoder fallback ([63a9eea](https://github.com/enriquezchristopher/CouchLink/commit/63a9eeaec0848e80d3db2e07c97d98afc44ff6b8))
* host streams its screen with chosen resolution and frame rate ([#38](https://github.com/enriquezchristopher/CouchLink/issues/38)) ([570962b](https://github.com/enriquezchristopher/CouchLink/commit/570962b32ede9f385b39a767472fad59d76511f1))
* release package includes FFmpeg 9 for host video, pinned and checksummed ([ab868b0](https://github.com/enriquezchristopher/CouchLink/commit/ab868b0033d71fb8af9acaf62fe2f1c740105e0c)), closes [#15](https://github.com/enriquezchristopher/CouchLink/issues/15)
* video stream protocol with FEC and keyframe requests ([#16](https://github.com/enriquezchristopher/CouchLink/issues/16)) ([ef41139](https://github.com/enriquezchristopher/CouchLink/commit/ef41139979894a0e2a4f3184f57a66e9bf9e6b74))


### Bug Fixes

* **core:** accept only shard headers the packetizer can produce ([f975d61](https://github.com/enriquezchristopher/CouchLink/commit/f975d6105c4a463c4852e7127382f51bdb1d8e2b)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **video:** keep frames on schedule and retry a failed encoder reopen ([26bb0d6](https://github.com/enriquezchristopher/CouchLink/commit/26bb0d6b4f2c60ce22b6e9c6d817da5906ac0965))
* **video:** report a missing FFmpeg DLL instead of crashing ([631695a](https://github.com/enriquezchristopher/CouchLink/commit/631695a61143c8f567a16e90b9d4796f8a54e4cb))

## [1.1.1](https://github.com/enriquezchristopher/CouchLink/compare/v1.1.0...v1.1.1) (2026-10-06)


### Bug Fixes

* **app:** lock slot and IP boxes after Join ([a8a34bb](https://github.com/enriquezchristopher/CouchLink/commit/a8a34bb1678309886000a8f70687c050333e5fca)), closes [#13](https://github.com/enriquezchristopher/CouchLink/issues/13)
* **pads:** stop removed pads from crashing the host ([3843bb8](https://github.com/enriquezchristopher/CouchLink/commit/3843bb87a22867e5c80ed22ed1023f6206dec33a)), closes [#12](https://github.com/enriquezchristopher/CouchLink/issues/12)
* v1.1 reliability fixes ([#9](https://github.com/enriquezchristopher/CouchLink/issues/9)-[#13](https://github.com/enriquezchristopher/CouchLink/issues/13)) ([c88975a](https://github.com/enriquezchristopher/CouchLink/commit/c88975a84ba1635a73e144985287fa2701c3d300))

## [1.1.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.0.0...v1.1.0) (2026-10-05)


### Features

* **app:** crash reports with dialog, pending-report notice and crash tests ([447298a](https://github.com/enriquezchristopher/CouchLink/commit/447298af47022226f3f37d7b4f1b53b7d8285ba5))
* **core:** crash report builder with redacted exception and log tail ([4057b0b](https://github.com/enriquezchristopher/CouchLink/commit/4057b0b4fbe4be5be2ffc778a2ec00100378f6f5))
* **core:** crash report store with fallback, pruning and shown tracking ([c4e206f](https://github.com/enriquezchristopher/CouchLink/commit/c4e206f7f4e141e009cdb5fdbae788f227117399))
* **core:** redactor for PC name, user name and IP addresses ([2b8b6ab](https://github.com/enriquezchristopher/CouchLink/commit/2b8b6ab52d6b1296d25c3d184ae6d4509ce6811c))
* **core:** rolling file log with in-memory tail ([9ec658e](https://github.com/enriquezchristopher/CouchLink/commit/9ec658e77bd53229c6802dc300e74da78a7d49af))
* crash reports ([#8](https://github.com/enriquezchristopher/CouchLink/issues/8)) ([f745c73](https://github.com/enriquezchristopher/CouchLink/commit/f745c731e4dacf0b0038b3ac63908982ed7c1b17))


### Bug Fixes

* **app:** make crash exit unable to hang on a second UI crash ([73b3970](https://github.com/enriquezchristopher/CouchLink/commit/73b3970efafbc3a34564540f3bfc8024381ee3de))
* **app:** open crash reports folder safely, include fallback location ([660a0e9](https://github.com/enriquezchristopher/CouchLink/commit/660a0e94f9b1356b4e6242c57d51dd9e33849ede))
* **core:** redact names at word boundaries, keep versions, invariant timestamps, safe prune ([80c2eb5](https://github.com/enriquezchristopher/CouchLink/commit/80c2eb5332bb9e599ba1a9ddc3bed47166749391))

## 1.0.0 (2026-10-05)


### Features

* **app:** dev window with Raw Input client and ViGEm host over UDP ([a6fe66d](https://github.com/enriquezchristopher/CouchLink/commit/a6fe66d3001353b354c041bc9a87ba9c884136eb))
* **core:** 24-byte input packet and newest-wins sequence filter ([38d0c12](https://github.com/enriquezchristopher/CouchLink/commit/38d0c12e95dd26fdae624f96df9880f79869e4d4))
* **core:** default key layout and keyboard/mouse to DS4 mapper ([f5aaec2](https://github.com/enriquezchristopher/CouchLink/commit/f5aaec2b714e7689fa60402471a2cbbecb9ce3b8))
* **core:** DS4 pad model, D-pad math and virtual pad interfaces ([1fb119e](https://github.com/enriquezchristopher/CouchLink/commit/1fb119e89a8ee743a06fc2b6fefc555c6b58e06e))
* **core:** keyboard stick math and decaying mouse right stick ([03f8903](https://github.com/enriquezchristopher/CouchLink/commit/03f8903d410a5b1e5c7c48ea0b27ff76988a40e9))
* **core:** pad manager with slot range, newest-wins and stale release ([d4c7e2e](https://github.com/enriquezchristopher/CouchLink/commit/d4c7e2e53b66c27f6e08be9ed953856ab41d94fd))
* **core:** send policy and UDP input sender/receiver ([fb89354](https://github.com/enriquezchristopher/CouchLink/commit/fb89354905d24aad0ba0172d6a00d0c74e2768a0))
* **pads:** ViGEm DS4 adapter, PadTest gate tool and gate results template ([7366814](https://github.com/enriquezchristopher/CouchLink/commit/73668143532afef439e2e6f4b8e9373b4e010931))
* **padtest:** game-free isolation check (PadTest check) reading pads back via HID ([4ab61d7](https://github.com/enriquezchristopher/CouchLink/commit/4ab61d7c033d8992e2ff7bcd94531c41aa1ac956))
* **padtest:** test every DS4 control on every pad in PadTest check ([086d05a](https://github.com/enriquezchristopher/CouchLink/commit/086d05ab098d16e3b87c93d6c1d1c5425ae0dfea))


### Bug Fixes

* keep host input running when a virtual pad call throws ([545c4ad](https://github.com/enriquezchristopher/CouchLink/commit/545c4adf44efd3f660bdeaa9e0bf00be1b060f91))
* release keys whose key-up was lost (Ctrl+Alt+Del, Win+L, UAC) ([60dbbfd](https://github.com/enriquezchristopher/CouchLink/commit/60dbbfd99edfbdd376af99ca17d42c94b09d28e7))
