with open('tests/TimeSink.Tests/Apps/AppLibraryTests.cs', 'r', encoding='utf-8') as f:
    text = f.read()

idx = text.find('    [Fact]\n    public void InspectUserDb()')
if idx == -1:
    idx = text.find('    [Fact]\r\n    public void InspectUserDb()')

base = text[:idx]
with open('tests/TimeSink.Tests/Apps/AppLibraryTests.cs', 'w', encoding='utf-8', newline='\r\n') as f:
    f.write(base.rstrip() + '\n}\n')
print('Removed InspectUserDb')

