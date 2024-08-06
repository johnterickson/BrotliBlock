using System.IO.Compression;
using System.Runtime.InteropServices;
using BrotliStream = NetFxLab.IO.Compression.BrotliStream;
using BrotliEncoderParameter = NetFxLab.IO.Compression.BrotliEncoderParameter;
using Brotli = NetFxLab.IO.Compression.Brotli;

namespace broccoli_sharp;

/*
https://github.com/dropbox/rust-brotli/pull/49#issuecomment-1804633560

Ok this is really cool! We have a scenario that is similar to https://dropbox.tech/infrastructure/-broccoli--syncing-faster-by-syncing-less where we our customers upload to a content-addressable-store.

Create a header block:
touch empty.bin
 --appendable --bytealign --bare -c empty.bin start.br
Individually compress the actual data blocks like this:
 --catable --bytealign --bare -c block001 block001.br
Then fake a ISLASTEMPTY metablock:
printf "\x03" > ~/end.br

Then you can both

Decompress individual blocks by a) prepending the start block and b) appending the end block: cat start.br block001.br end.br | brotli -d
A standard decompressor (e.g. curl --compressed) can recreate the whole file from the concatenation of all the compressed blocks: e.g. cat start.br block*.br end.br | brotli -d
If you need to, rearrange the compressed blocks order to rearrange the output order

*/
