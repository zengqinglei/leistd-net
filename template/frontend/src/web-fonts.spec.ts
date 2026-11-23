describe('Web fonts', () => {
  it('registers each text weight used by the interface', () => {
    const weights = [...document.fonts]
      .filter((face) => face.family.includes('Geist'))
      .map((face) => face.weight);
    expect(weights).toEqual(expect.arrayContaining(['400', '500', '600']));
  });

  // Spartan 预设的 --font-mono 是自引用，样式表不定义时 font-mono 落回正文字体 Geist
  it('renders font-mono in Geist Mono', () => {
    const weights = [...document.fonts]
      .filter((face) => face.family.replaceAll(/["']/g, '') === 'Geist Mono')
      .map((face) => face.weight);
    expect(weights).toEqual(expect.arrayContaining(['400', '500']));

    const code = document.createElement('code');
    code.className = 'font-mono';
    document.body.append(code);
    onTestFinished(() => code.remove());
    expect(getComputedStyle(code).fontFamily).toMatch(/^"Geist Mono",/);
  });
});
