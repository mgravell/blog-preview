(general TODO: add some color and graphics)

# Migrating from Redis-64 to Memurai

Whereby I present the history of Redis-64, along with options and motivations for Redis-64 users on Windows to consider updating their redis via Memurai. 

## Running Redis on Windows, 2021 edition; replacing Redis-64

A funny thing happened recently; after updating to .NET 6, some StackExchange.Redis users started reporting that redis was not working
from their web applications. A relatively small number, so: not an endemic fail - but also far from zero. As you might hope, we took
a look, and pieced together that what was *actually* happening here was:

- a part of ASP.NET allows using redis as a cache
- historically, this used the `HMSET` redis command (which sets multiple hash fields, contrast to `HSET` which sets a single hash field)
- in redis 4.0 (July 2014), `HSET` was made variadic and thus functionally identical to `HMSET` - and so `HMSET` was marked "deprecated" (although it still works)
- respecting the "deprecated" marker, .NET 6 (Nov 2021) included a change to switch from `HMSET` to `HSET`, thinking that the number of people below redis 4.0 should be negligible
- and it turned out not to be!

This problem [was reported](https://github.com/dotnet/aspnetcore/issues/38715) and the relevant code has now been fixed [to support both variants](https://github.com/dotnet/aspnetcore/pull/38927), but we need to take a step further and understand why a non-trivial number of users are *more than 7 years behind on servicing*. After a bit more probing, it is my understanding that for a lot of the affected users, the answer is simple: they are using Redis-64.

## What is (was) Redis-64?

Historically, the main redis project has only supported linux usage. There are some particular nuances of how redis is implemented (using
fork-based replication and persistance with copy-on-write semantics, for example) that don't make for a direct "just recompile the code and it works the same" nirvana. Way back around the redis 2.6 era (2013), Microsoft (in the guise of MSOpenTech) released a viable Windows-compatible fork, under the name Redis-64 (May 2013). This fork was kept up to date through redis 2.8 and some 3.0 releases, but the development was ultimately dropped some time in 2016, leaving redis 3.0 as the last MSOpenTech redis implementation. There was also a Redis-32 variant for x86 usage, although this was even more short-lived, staying at 2.6.

I'm all in favor of a wide variety of good quality tools and options. If you want to run a redis server as part of a Windows installation, you should be able to do that! This could be because you already have Windows servers and administrative experience, and want a single OS deployment; it could be because you don't want the additional overheads/complications of virtualization/container technologies. It could be because you're primarily doing development on a Windows machine, and it is convenient. Clearly, Redis-64 was an attractive option to many people who want to run redis natively on Windows; I know we used it (in addition to redis on linux) when I worked with Stack Overflow.

## Running outdated software is a risk

Ultimately, being stuck with a server that is based on 2015/2016 starts to present a few problems:

1. you need to live with long-known and long-fixed bugs and other problems (including any well-known security vulnerabilities)
2. you don't get to use up-to-date features and capabilities
3. you might start dropping off the support horizon of 3rd party libraries and tools

This 3rd option is what happened with ASP.NET in .NET 6, but the other points also stand; the "modules" (redis 4.x) and "streams" (redis 5.x) features come to mind immediately - both have huge utility.

So: if you're currently using Redis-64, how can we resolve this, without completely changing our infrastructure?

## Shout-out: Memurai

The simplest way out of this corner is, in my opinion: Memurai, by Janea Systems. So: what is Memurai? To put it simply: Memurai is a redis 5 compatible fork of redis that runs natively on Windows. That means you get a wide range of more recent redis fixes and features. Fortunately, it is [a breeze to install](https://www.memurai.com/blog/install-redis-windows-alternatives-such-as-memurai), with options for [nuget](https://www.nuget.org/packages/MemuraiDeveloper/), [choco/cinst](https://community.chocolatey.org/packages/memurai-developer/), [winget](https://winget.run/pkg/Memurai/MemuraiDeveloper), [winstall](https://winstall.app/apps/Memurai.MemuraiDeveloper) and [an installer](https://www.memurai.com/get-memurai). This means that you can get started with a Memurai development installation immediately.

The obsolete Redis-64 nuget package also now carries a link to Memurai in the  "Suggested Alternatives", which is encouraging. To be transparent: I need to emphasize - Memurai is a commercial offering with a free developer edition. If we look at how Redis-64 ultimately stagnated, I view this as a strength: it means that someone has a vested interest in making sure that the product continues to evolve and be supported, now and into the future.

## Working with Memurai

As previously noted: installation is quick and simple, but so is working with it. The command-line tools change nominally; instead of `redis-cli`, we have `memurai-cli`; instead of `redis-server` we have `memurai`. However, they work exactly as you expect and will be immediately familar to anyone who has used redis. At the server level, Memurai surfaces the exact same protocol and API surface as a vanilla redis server, meaning any existing redis-compatible tools and clients should work without problem:


``` txt
c:\Code>memurai-cli
127.0.0.1:6379> get foo
(nil)
127.0.0.1:6379> set foo bar
OK
127.0.0.1:6379> get bar
(nil)
127.0.0.1:6379>
```

(note that `redis-cli` would have worked identically)

At the metadata level, you may notice that `info server` reports some additional antries:

``` txt
127.0.0.1:6379> info server
# Server
memurai_edition:Memurai Developer
memurai_version:2.0.5
redis-version:5.0.14
...
```

The `redis_version` entry is present so that client libraries and applications expecting this entry can understand the features available, so this is effectively the redis API compatibility level; the `memurai_version` and `memurai_edition` give specific Memurai information, if you need it - but other than those additions (and extra rows are expected here), everything works as you would expect. For example, we can use any pre-existing redis client to talk to the server:

``` c#
using StackExchange.Redis;

// connect to local redis, default port
using var conn = await ConnectionMultiplexer.ConnectAsync("127.0.0.1");
var db = conn.GetDatabase();

// reset and populate some data
await db.KeyDeleteAsync("mykey");
for (int i = 1; i <= 20; i++)
{
    await db.StringIncrementAsync("mykey", i);
}

// fetch and display
var sum = (int)await db.StringGetAsync("mykey");
Console.WriteLine(sum); // writes: 210
```

Configuring the server works exactly like it does for redis - the config file works the same, although the example template is named differently:

```
c:\Code>where memurai
C:\Program Files\Memurai\memurai.exe

c:\Code>dir "C:\Program Files\Memurai\*.conf" /B
memurai.conf
```

## Summary

Putting this all together: if you're currently using Redis-64 to run a redis server natively on Windows, then Memurai might make a very appealing option - almost certainly more appealing than remaining on the long-obsolete Redis-64. All of your existing redis knowledge continues to apply, but you get a wide range of features that were added to redis after Redis-64 was last maintained.