describe('Web fonts', () => {
  it('registers each text weight used by the interface', () => {
    const weights = [...document.fonts]
      .filter((face) => face.family.includes('Geist'))
      .map((face) => face.weight);
    expect(weights).toEqual(expect.arrayContaining(['400', '500', '600']));
  });
});
