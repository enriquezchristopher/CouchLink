# Changelog

## [1.2.0](https://github.com/enriquezchristopher/CouchLink/compare/v1.1.1...v1.2.0) (2026-10-06)


### Features

* **app:** stream a test pattern from host to clients ([640a21c](https://github.com/enriquezchristopher/CouchLink/commit/640a21c0c47b5ffdd828077c87968e3f6456702d)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** drop undecodable frames and request keyframes after loss ([8c4e377](https://github.com/enriquezchristopher/CouchLink/commit/8c4e377936c257e522279d0ae096fe735f205714)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** encoded frame source interface and test pattern ([56d58a9](https://github.com/enriquezchristopher/CouchLink/commit/56d58a9eed99ec964ed252c454081abf2acc6efe)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** host keyframe pacing and stream targets ([ef3830e](https://github.com/enriquezchristopher/CouchLink/commit/ef3830ed057665e81c8ec1a59b5126dc4ec87811)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** host video streamer and client video pipeline ([bb272a1](https://github.com/enriquezchristopher/CouchLink/commit/bb272a13d84aeef714c198d71e94f94de83f70cd)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** reassemble and repair frames from shard packets ([1d98348](https://github.com/enriquezchristopher/CouchLink/commit/1d9834822b36432d30faa2533878b2693b54dbce)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** Reed-Solomon erasure code over GF(256) ([8677410](https://github.com/enriquezchristopher/CouchLink/commit/8677410776f7687124648db5d6e0b7d932012876)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** split encoded frames into FEC-protected shard packets ([99e6457](https://github.com/enriquezchristopher/CouchLink/commit/99e6457fe501511b7eaaad5aa9a9b7c077f2cb2e)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** UDP transport for video and keyframe requests ([15c8803](https://github.com/enriquezchristopher/CouchLink/commit/15c8803fa47c4f644a347509d845009aef99beca)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* **core:** video shard and keyframe request packets ([bc1dad5](https://github.com/enriquezchristopher/CouchLink/commit/bc1dad5537237f5545c11ca6889531f384fa4546)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)
* video stream protocol with FEC and keyframe requests ([#16](https://github.com/enriquezchristopher/CouchLink/issues/16)) ([ef41139](https://github.com/enriquezchristopher/CouchLink/commit/ef41139979894a0e2a4f3184f57a66e9bf9e6b74))


### Bug Fixes

* **core:** accept only shard headers the packetizer can produce ([f975d61](https://github.com/enriquezchristopher/CouchLink/commit/f975d6105c4a463c4852e7127382f51bdb1d8e2b)), closes [#16](https://github.com/enriquezchristopher/CouchLink/issues/16)

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
