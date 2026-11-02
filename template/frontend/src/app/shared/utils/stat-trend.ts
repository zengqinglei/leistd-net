import { HLM_CHART_THEME } from '@spartan-ng/helm/chart';
import { areaY, defineChart, lineY } from '@tanstack/charts';
import { scaleLinear } from '@tanstack/charts/scales/linear';

/**
 * 与上一区间相比的变化率（比例，0.29 表示 +29%），按整数百分比取整：显示与徽章配色用同一个值，
 * 不会出现"0%"却标成上升。上一区间没有值或为 0 时不比较。
 */
export function changeOf(value: number, previous: number | null | undefined): number | null {
  if (previous == null || previous === 0) {
    return null;
  }
  return Math.round(((value - previous) / previous) * 100) / 100;
}

/**
 * 指标卡里的迷你趋势：去掉坐标轴与网格，只留面积与线。空桶（`null`，如成功率分母为 0）不画点，
 * 不到两个点画不出趋势。
 */
export function sparklineOf(series: readonly (number | null)[] | null | undefined, color: string) {
  const rows = (series ?? []).flatMap((value, index) => (value === null ? [] : [{ index, value }]));
  if (rows.length < 2) {
    return null;
  }
  const values = rows.map((row) => row.value);
  // 纵轴取序列自身的最小、最大值，不从 0 起：存量这类大数的小起伏才看得出。不变的序列上下各留一格，
  // 画在中线上——贴底的线会被读成"降到了 0"。有起伏时面积的底边取纵轴下限，否则默认从 0 填到绘图区外
  const flat = Math.min(...values) === Math.max(...values);
  const low = Math.min(...values) - (flat ? 1 : 0);
  const high = Math.max(...values) + (flat ? 1 : 0);
  return {
    definition: defineChart({
      guides: false,
      margin: 2,
      // 不变的序列只画线：面积会在中线下铺出一条色带，像是一块数值
      marks: [
        ...(flat
          ? []
          : [areaY(rows, { x: 'index', y: 'value', y1: low, fill: color, fillOpacity: 0.12 })]),
        lineY(rows, { x: 'index', y: 'value', stroke: color, strokeWidth: 1.75 }),
      ],
      scales: {
        x: { scale: scaleLinear },
        y: { scale: scaleLinear().domain([low, high]) },
      },
      theme: HLM_CHART_THEME,
    }),
    ariaLabel: '',
    height: 34,
  };
}
