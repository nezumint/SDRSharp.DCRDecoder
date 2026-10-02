int checks = 0;
HostAudioTests.Run((passed, description) =>
{
    if (!passed) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine($"PASS: {description}");
});
Console.WriteLine($"Passed {checks} self-contained audio tests.");
