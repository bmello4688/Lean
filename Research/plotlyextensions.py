import plotly.graph_objects as go
from plotly.subplots import make_subplots
import pandas as pd
from ipywidgets import widgets

def __get_ohlc_chart_data(df: pd.DataFrame):

    trace = None
    if {'open', 'high', 'low', 'close'}.issubset(df.columns):

        trace = go.Candlestick(x=df.index,
                    open=df['open'], high=df['high'],
                    low=df['low'], close=df['close'], name="OHLC")

    return trace

def __get_volume_chart_data(df: pd.DataFrame):

    trace = None
    if {'volume'}.issubset(df.columns):
        trace = go.Bar(name="Volume", x=df.index, y=df['volume'], opacity=0.5, marker={"color":"rgb(7, 89, 148)"})

    return trace

def __single_symbol_plot(df: pd.DataFrame, pre_title_text):
    fig = make_subplots(specs=[[{"secondary_y": True}]])

    ohlc = __get_ohlc_chart_data(df)
    title = ""

    if ohlc:
        fig.add_trace(ohlc, secondary_y=False)
        fig.update_layout(xaxis_rangeslider_visible=False)
        title=ohlc.name

    volume = __get_volume_chart_data(df)

    if volume:
        fig.add_trace(volume, secondary_y=True)
        if title == "":
            title=volume.name
        else:
            title+="V"

    fig.update_layout(title=f'{pre_title_text}{title}', title_x=0.5)
    fig.update_layout(plot_bgcolor='rgb(255, 255, 255)')
    fig.update_layout(yaxis={
          "tickcolor": "rgba(0,0,0,0.75)",
      
          "gridcolor": "rgba(0,0,0,0.25)",
      
          "zerolinecolor": "black",
        })

    return fig


def __validate_and_return_figures(plot):
    if isinstance(plot, widgets.VBox):
        figs = plot.children
    elif isinstance(plot, go.FigureWidget):
        figs = [plot]
    elif isinstance(plot, list) and isinstance(plot[0], go.FigureWidget):# list of figure widgets
        figs = plot
    else:
        raise ValueError(f"Incorrect type {type(plot)}: needs to be a FigureWidget, a list of FigureWidgets or VBox of figure widgets")

    if len(figs) == 0:
        raise ValueError("plot has no data")

    return figs

def save_plot_as_html(plot, filename="dashboard.html"):
    dashboard = open(filename, 'w')
    dashboard.write("<html><head></head><body>\n")

    figs = __validate_and_return_figures(plot)

    for fig in figs:
        inner_html = fig.to_html().split('<body>')[1].split('</body>')[0]
        dashboard.write(inner_html)


    dashboard.write("</body></html>\n")

def plot(df: pd.DataFrame, pre_title_text: str = ''):
    if isinstance(df.index, pd.MultiIndex):
        unique_symbols = set(df.index.get_level_values(0))

        children = []
        #create chart per symbol
        for symbolAndFreq in unique_symbols:
            symbol = symbolAndFreq.split()[0]
            fig = __single_symbol_plot(df.loc[symbolAndFreq], f'{pre_title_text}{symbol} ')
            children.append(go.FigureWidget(fig))

        dashboard = widgets.VBox(children)

        return dashboard

    else:
        figure = go.FigureWidget(__single_symbol_plot(df, pre_title_text))

        return figure

def add_to_plot(plot, trace, specific_graph_identifier_by_title=None):
    
    figs = __validate_and_return_figures(plot)
        
    if len(figs) == 1:
        figs[0].add_traces(trace)
    elif specific_graph_identifier_by_title:
        traceAdded = False
        titles = []
        for fig in figs:
            titles.append(fig.layout.title.text)
            if specific_graph_identifier_by_title in fig.layout.title.text:
                fig.add_traces(trace)
                traceAdded = True
                break #only apply to one graph
        if not traceAdded:
            raise ValueError(f"specific_graph_identifier_by_title={specific_graph_identifier_by_title} was not in any graph title. Options are {titles}")
    else:
        raise ValueError("specific_graph_identifier_by_title cannot be None when passing in multiple graphs")
