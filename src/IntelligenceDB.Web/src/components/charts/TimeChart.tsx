import { BarChart, LineChart } from "echarts/charts";
import { DataZoomComponent, GridComponent, LegendComponent, TooltipComponent } from "echarts/components";
import * as echarts from "echarts/core";
import { CanvasRenderer } from "echarts/renderers";
import { useEffect, useRef } from "react";
import { useTheme } from "@/lib/theme";

echarts.use([LineChart, BarChart, GridComponent, TooltipComponent, LegendComponent, DataZoomComponent, CanvasRenderer]);

export interface ChartSeries {
  name: string;
  /** [ISO time, value] pairs. */
  data: [string, number | null][];
  color?: string;
  area?: boolean;
  type?: "line" | "bar";
  stack?: string;
}

export const chartColors = ["#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6", "#06b6d4"];

const themes = {
  light: { text: "#6b7280", grid: "#e5e7eb", tooltipBg: "#ffffff", tooltipText: "#111827" },
  dark: { text: "#a1a1aa", grid: "#2f3340", tooltipBg: "#1f2230", tooltipText: "#f3f4f6" },
};

interface TimeChartProps {
  series: ChartSeries[];
  height?: number;
  unit?: string;
  yMax?: number;
  /** Fixed decimals in tooltips / axis. */
  decimals?: number;
}

/** Time-series chart (Apache ECharts). Follows dark/light mode and resizes with its container. */
export function TimeChart({ series, height = 220, unit = "", yMax, decimals = 0 }: TimeChartProps) {
  const ref = useRef<HTMLDivElement>(null);
  const chart = useRef<echarts.ECharts | null>(null);
  const { resolved } = useTheme();

  useEffect(() => {
    if (!ref.current) return;
    const c = echarts.init(ref.current, undefined, { renderer: "canvas" });
    chart.current = c;
    const ro = new ResizeObserver(() => c.resize());
    ro.observe(ref.current);
    return () => {
      ro.disconnect();
      c.dispose();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    const c = chart.current;
    if (!c) return;
    const t = themes[resolved];
    const fmt = (v: number | null | undefined) =>
      v == null ? "-" : `${Number(v).toLocaleString(undefined, { maximumFractionDigits: decimals })}${unit}`;

    c.setOption(
      {
        animation: false,
        color: series.map((s, i) => s.color ?? chartColors[i % chartColors.length]),
        grid: { left: 8, right: 16, top: series.length > 1 ? 32 : 12, bottom: 8, containLabel: true },
        legend: series.length > 1 ? { top: 0, textStyle: { color: t.text }, icon: "roundRect", itemWidth: 12, itemHeight: 8 } : undefined,
        tooltip: {
          trigger: "axis",
          backgroundColor: t.tooltipBg,
          borderColor: t.grid,
          textStyle: { color: t.tooltipText, fontSize: 12 },
          valueFormatter: (v: unknown) => fmt(typeof v === "number" ? v : null),
        },
        xAxis: {
          type: "time",
          axisLine: { lineStyle: { color: t.grid } },
          axisLabel: { color: t.text, hideOverlap: true },
          splitLine: { show: false },
        },
        yAxis: {
          type: "value",
          max: yMax,
          axisLabel: { color: t.text, formatter: (v: number) => fmt(v) },
          splitLine: { lineStyle: { color: t.grid } },
        },
        series: series.map((s) => ({
          name: s.name,
          type: s.type ?? "line",
          stack: s.stack,
          showSymbol: false,
          smooth: false,
          connectNulls: false,
          lineStyle: { width: 1.5 },
          areaStyle: s.area ? { opacity: 0.15 } : undefined,
          data: s.data.map(([time, value]) => [new Date(time.endsWith("Z") ? time : `${time}Z`).getTime(), value]),
        })),
      },
      true,
    );
  }, [series, resolved, unit, yMax, decimals]);

  return <div ref={ref} style={{ height, width: "100%" }} />;
}
