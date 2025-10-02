# BigCity

This project is a technology demo. It demonstrates how to use TeamCity API to compile a Visual Studio project using several configurations (possibly on different agents).

The program reads the solution's project dependency graph and creates relevant project configurations. Those project configurations have defined artifact dependencies so that the separate parts of a solution can be assembled correctly.

The performance benefits of this approach only show up when you have a solution with a large number of projects, and those projects are heavily inter-dependent. Keep in mind there are time costs related to transfering the artifact dependencies from one agent to another.

This demo is quite old; it may or may not work in recent versions of TeamCity.
