# Changelog

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
