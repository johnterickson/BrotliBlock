# block-compressable `brotli`

## Background
https://github.com/dropbox/rust-brotli/pull/49#issuecomment-1804633560

Ok this is really cool! We have a scenario that is similar to https://dropbox.tech/infrastructure/-broccoli--syncing-faster-by-syncing-less where we our customers upload to a content-addressable-store.

## What is this?
This library (and toy app) lets you split a file into blocks, compress each block individually, store each block individually, decompress each block individually, but ALSO you can byte-concatenate the compressed blocks to create a stream that is standard `brotli` stream that can be decompressed by normal tools (e.g. `curl`, `brotli -d`).

### Compressed block types

The root of the idea is to separate the creation of brotli a) header, b) content, and c) EOF marker.

Let's explore this with some simple examples:
```
echo hello>hello.txt
echo world>world.txt
```

### Option 1: Only create bare/Middle blocks
The simplest approach is to only create "bare" blocks - blocks with neither the header nor the EOF marker:
```
BrotliBlock -b -c -o hello.txt.br.bare hello.txt
BrotliBlock -b -c -o world.txt.br.bare world.txt
```
However, to make a standard `brotli` stream, a header and EOF must be added.  This can be done with the `-b` flag:
```
BrotliBlock -b hello.txt.br.bare
hello

BrotliBlock -b world.txt.br.bare
world

BrotliBlock -b hello.txt.br.bare world.txt.br.bare
hello
world
```

### Option 2: First/Middle/Last blocks and byte-wise concatenation
Usually, though, we know which is the first block and which is the last block and we can add the header to the beginning of the first block, add the EOF to the end of the last block, and leave the middle blocks as bare:
```
BrotliBlock.exe -c -o hello.txt.br.first --position First hello.txt
BrotliBlock.exe -c -o world.txt.br.last --position Last world.txt
```
And the concatenation decompresses normally:
```
BrotliBlock.exe hello.txt.br.first world.txt.br.last
hello
world

$ cat hello.txt.br.first world.txt.br.last | brotli -d
hello
world
```
However, some care is needed when one wants to decompress individual blocks - one must specify if it is First, Middle, or Last:
```
BrotliBlock.exe --position First hello.txt.br.first
hello

BrotliBlock.exe --position Last world.txt.br.last
world
```

NOTE: Because the header contains the window size of the `brotli` encoding, all blocks must be compressed with the same window size.

## Appendix: for the Rust version

Create a header block:
```
touch empty.bin
 --appendable --bytealign --bare -c empty.bin start.br
```
Individually compress the actual data blocks like this:
```
 --catable --bytealign --bare -c block001 block001.br
```
Then fake a ISLASTEMPTY metablock:
```
printf "\x03" > ~/end.br
```

Then you can both

Decompress individual blocks by a) prepending the start block and b) appending the end block:
```
cat start.br block001.br end.br | brotli -d
```
A standard decompressor (e.g. curl --compressed) can recreate the whole file from the concatenation of all the compressed blocks: e.g.
```
cat start.br block*.br end.br | brotli -d
```
If you need to, rearrange the compressed blocks order to rearrange the output order
